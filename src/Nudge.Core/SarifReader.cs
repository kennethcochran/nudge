using System.Text.Json;

namespace Nudge;

/// <summary>
/// Reads a SARIF 2.1.0 log (the OASIS interchange format) into the engine's
/// finding records. This is the adapter for the channels the MSBuild event
/// stream cannot carry: the compiler's ErrorLog carries the complete
/// compiler + analyzer stream at every severity — including the
/// <c>note</c> (Info) and <c>none</c> levels that never enter the event
/// stream at all — and standalone tools (InspectCode, CodeQL, Semgrep, …)
/// emit native SARIF.
///
/// Malformed input fails loudly (see <see cref="Parse"/>): a SARIF file the
/// reader cannot understand must surface as an error, never as a clean
/// report with the findings silently dropped.
/// </summary>
public static class SarifReader
{
    /// <summary>
    /// Parses SARIF log text into diagnostics, deduped on the same key as
    /// the text log parser (rule, file, line, message — multi-targeted and
    /// multi-run logs repeat findings).
    /// </summary>
    /// <param name="sarifText">The SARIF JSON text.</param>
    /// <param name="baseDirectory">
    /// Directory that relative artifact URIs resolve against (typically the
    /// directory the SARIF file lives in, or the repo root for an ErrorLog).
    /// When null, relative URIs are kept as written.
    /// </param>
    /// <exception cref="JsonException">The text is not valid JSON.</exception>
    /// <exception cref="FormatException">
    /// The JSON is not a SARIF 2.1.0 log (no <c>runs</c> array, or an
    /// unsupported <c>version</c>).
    /// </exception>
    public static IReadOnlyList<BuildDiagnostic> Parse(string sarifText, string? baseDirectory = null)
    {
        using var doc = JsonDocument.Parse(sarifText);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("runs", out var runs)
            || runs.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Not a SARIF log: the document has no 'runs' array.");
        }
        if (root.TryGetProperty("version", out var version)
            && version.ValueKind == JsonValueKind.String
            && version.GetString() != "2.1.0")
        {
            throw new FormatException(
                $"Unsupported SARIF version '{version.GetString()}': nudge reads SARIF 2.1.0 logs.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<BuildDiagnostic>();

        foreach (var run in runs.EnumerateArray())
        {
            var rules = ReadRules(run);
            if (!run.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            foreach (var sarifResult in results.EnumerateArray())
            {
                var diag = ConvertResult(sarifResult, rules, baseDirectory);
                var key = $"{diag.RuleId}|{diag.File}|{diag.Line}|{diag.Message}";
                if (seen.Add(key))
                    result.Add(diag);
            }
        }

        return result;
    }

    /// <summary>Reads and parses a SARIF file from disk.</summary>
    public static IReadOnlyList<BuildDiagnostic> ReadFile(string path, string? baseDirectory = null) =>
        Parse(File.ReadAllText(path), baseDirectory);

    /// <summary>
    /// The rule metadata of one run: the ordered descriptor list (for
    /// <c>ruleIndex</c> lookups) plus default levels and message strings
    /// keyed by rule ID.
    /// </summary>
    private sealed record RuleSet(
        IReadOnlyList<string> IdsByIndex,
        IReadOnlyDictionary<string, string?> DefaultLevelById,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> MessageStringsById,
        IReadOnlyDictionary<string, string> GlobalMessageStrings);

    private static RuleSet ReadRules(JsonElement run)
    {
        var ids = new List<string>();
        var levels = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var messageStrings = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var globalStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (run.TryGetProperty("tool", out var tool)
            && tool.TryGetProperty("driver", out var driver))
        {
            if (driver.TryGetProperty("globalMessageStrings", out var globals))
                ReadMessageStrings(globals, globalStrings);

            if (driver.TryGetProperty("rules", out var rules)
                && rules.ValueKind == JsonValueKind.Array)
            {
                foreach (var rule in rules.EnumerateArray())
                {
                    var id = GetString(rule, "id") ?? string.Empty;
                    ids.Add(id);
                    if (id.Length == 0)
                        continue;
                    levels[id] = rule.TryGetProperty("defaultConfiguration", out var config)
                        ? GetString(config, "level")
                        : null;
                    if (rule.TryGetProperty("messageStrings", out var strings))
                    {
                        var perRule = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        ReadMessageStrings(strings, perRule);
                        messageStrings[id] = perRule;
                    }
                }
            }
        }

        return new RuleSet(ids, levels, messageStrings, globalStrings);
    }

    private static void ReadMessageStrings(JsonElement container, Dictionary<string, string> target)
    {
        if (container.ValueKind != JsonValueKind.Object)
            return;
        foreach (var entry in container.EnumerateObject())
        {
            var text = GetString(entry.Value, "text");
            if (text != null)
                target[entry.Name] = text;
        }
    }

    private static BuildDiagnostic ConvertResult(JsonElement result, RuleSet rules, string? baseDirectory)
    {
        var ruleId = GetString(result, "ruleId")
            ?? (result.TryGetProperty("rule", out var ruleRef) ? GetString(ruleRef, "id") : null)
            ?? RuleIdAtIndex(result, rules)
            ?? "unknown";

        // Severity: the result's own level wins, then the rule descriptor's
        // default level, then the SARIF default (warning). The note/none
        // levels are preserved, not promoted to warning — they are the
        // whole reason this channel exists.
        var level = GetString(result, "level")
            ?? (rules.DefaultLevelById.TryGetValue(ruleId, out var defaultLevel) ? defaultLevel : null)
            ?? "warning";

        var message = ReadMessage(result, ruleId, rules);

        string? file = null;
        var line = 0;
        var column = 0;
        if (result.TryGetProperty("locations", out var locations)
            && locations.ValueKind == JsonValueKind.Array
            && locations.GetArrayLength() > 0
            && locations[0].TryGetProperty("physicalLocation", out var physical))
        {
            if (physical.TryGetProperty("artifactLocation", out var artifact))
                file = ResolveUri(GetString(artifact, "uri"), baseDirectory);
            if (physical.TryGetProperty("region", out var region))
            {
                line = GetInt(region, "startLine");
                column = GetInt(region, "startColumn");
            }
        }

        return new BuildDiagnostic(
            file,
            line,
            column,
            level.ToLowerInvariant(),
            ruleId,
            message,
            Project: null);
    }

    private static string? RuleIdAtIndex(JsonElement result, RuleSet rules)
    {
        if (result.TryGetProperty("ruleIndex", out var index)
            && index.ValueKind == JsonValueKind.Number
            && index.TryGetInt32(out var i)
            && i >= 0 && i < rules.IdsByIndex.Count
            && rules.IdsByIndex[i].Length > 0)
        {
            return rules.IdsByIndex[i];
        }
        return null;
    }

    private static string ReadMessage(JsonElement result, string ruleId, RuleSet rules)
    {
        if (!result.TryGetProperty("message", out var message))
            return string.Empty;

        var text = GetString(message, "text");
        if (text == null)
        {
            // No inline text: resolve the message id against the rule's
            // messageStrings, then the driver's globalMessageStrings.
            var id = GetString(message, "id");
            if (id != null)
            {
                if (rules.MessageStringsById.TryGetValue(ruleId, out var perRule)
                    && perRule.TryGetValue(id, out var fromRule))
                {
                    text = fromRule;
                }
                else if (rules.GlobalMessageStrings.TryGetValue(id, out var fromGlobal))
                {
                    text = fromGlobal;
                }
            }
            text ??= id ?? string.Empty;
        }

        // Substitute {0}, {1}, … placeholders from the message arguments.
        if (message.TryGetProperty("arguments", out var arguments)
            && arguments.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var arg in arguments.EnumerateArray())
            {
                var value = arg.ValueKind == JsonValueKind.String ? arg.GetString() ?? string.Empty : arg.GetRawText();
                text = text.Replace("{" + i + "}", value, StringComparison.Ordinal);
                i++;
            }
        }

        return text;
    }

    /// <summary>
    /// Turns an artifactLocation URI into a filesystem path: <c>file://</c>
    /// URIs are decoded (percent-escapes included), plain relative paths are
    /// kept as written unless a base directory was supplied.
    /// </summary>
    private static string? ResolveUri(string? uri, string? baseDirectory)
    {
        if (string.IsNullOrEmpty(uri))
            return null;

        if (uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(uri, UriKind.Absolute, out var fileUri))
        {
            return fileUri.LocalPath;
        }

        var path = Uri.UnescapeDataString(uri);
        if (baseDirectory != null && !Path.IsPathRooted(path))
            return Path.GetFullPath(Path.Combine(baseDirectory, path));
        return path;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetInt(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var i)
            ? i
            : 0;
}
