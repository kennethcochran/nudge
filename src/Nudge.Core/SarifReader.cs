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
/// report with the findings silently dropped. Validation follows the SARIF
/// schema: <c>version</c> must be exactly "2.1.0", every run must carry a
/// <c>results</c> array, and every result must carry a resolvable
/// <c>message</c> — anything else is not a completed analysis.
/// </summary>
public static class SarifReader
{
    /// <summary>
    /// Parses SARIF log text into diagnostics, deduped on the same key as
    /// the text log parser (rule, file, line, message — multi-targeted and
    /// multi-run logs repeat findings). Duplicates keep the highest
    /// severity seen, so a finding reported as both warning and error is
    /// never downgraded by deduplication. Results whose <c>kind</c> is
    /// <c>pass</c> or <c>notApplicable</c> are not findings and are skipped.
    /// </summary>
    /// <param name="sarifText">The SARIF JSON text.</param>
    /// <param name="baseDirectory">
    /// Directory that relative artifact URIs resolve against when the log
    /// itself gives no base (a location's <c>uriBaseId</c>, resolved through
    /// the run's <c>originalUriBaseIds</c>, wins when present). Typically
    /// the directory the SARIF file lives in, or the repo root for an
    /// ErrorLog. When null, unresolved relative URIs are kept as written.
    /// </param>
    /// <exception cref="JsonException">The text is not valid JSON.</exception>
    /// <exception cref="FormatException">
    /// The JSON is not a SARIF 2.1.0 log: missing or unsupported
    /// <c>version</c>, no <c>runs</c> array, a run without a <c>results</c>
    /// array, a non-object run/result element, or a result without a
    /// resolvable <c>message</c>.
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
        // SARIF requires version, and the only value it may carry is
        // "2.1.0". A log without it is not a SARIF 2.1.0 log this reader
        // can vouch for — accepting {"runs":[]} as "clean" would be a
        // silent failure.
        if (!root.TryGetProperty("version", out var version)
            || version.ValueKind != JsonValueKind.String
            || version.GetString() != "2.1.0")
        {
            throw new FormatException(
                "Not a SARIF 2.1.0 log: 'version' is missing or is not \"2.1.0\".");
        }

        var indexByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<BuildDiagnostic>();

        foreach (var run in runs.EnumerateArray())
        {
            if (run.ValueKind != JsonValueKind.Object)
                throw new FormatException("Invalid SARIF log: a run is not an object.");
            var runInfo = ReadRunInfo(run);
            // The SARIF schema requires every run to carry its results
            // array (possibly empty). A missing or non-array value means
            // the run is malformed or never completed — not a clean run.
            if (!run.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException(
                    "Invalid SARIF log: a run has no 'results' array.");
            }
            foreach (var sarifResult in results.EnumerateArray())
            {
                if (sarifResult.ValueKind != JsonValueKind.Object)
                    throw new FormatException("Invalid SARIF log: a result is not an object.");
                // A passed or inapplicable check is not a finding.
                var kind = GetString(sarifResult, "kind");
                if (kind is "pass" or "notApplicable")
                    continue;

                var diag = ConvertResult(sarifResult, runInfo, baseDirectory);
                var key = $"{diag.RuleId}|{diag.File}|{diag.Line}|{diag.Message}";
                if (indexByKey.TryGetValue(key, out var existingIndex))
                {
                    // Merge duplicates without losing the highest severity:
                    // the same finding reported as warning in one run and
                    // error in another is an error.
                    if (SeverityRank(diag.Severity) > SeverityRank(result[existingIndex].Severity))
                        result[existingIndex] = diag;
                }
                else
                {
                    indexByKey[key] = result.Count;
                    result.Add(diag);
                }
            }
        }

        return result;
    }

    /// <summary>Reads and parses a SARIF file from disk.</summary>
    public static IReadOnlyList<BuildDiagnostic> ReadFile(string path, string? baseDirectory = null) =>
        Parse(File.ReadAllText(path), baseDirectory);

    private static int SeverityRank(string severity) => severity.ToLowerInvariant() switch
    {
        "error" => 4,
        "warning" => 3,
        "note" => 2,
        "none" => 1,
        _ => 0,
    };

    /// <summary>
    /// The per-run context a result resolves against: rule metadata (the
    /// ordered descriptor list for <c>ruleIndex</c> lookups, default levels
    /// and message strings keyed by rule ID) and the run's
    /// <c>originalUriBaseIds</c> for resolving <c>uriBaseId</c> references.
    /// </summary>
    private sealed record RunInfo(
        IReadOnlyList<string> IdsByIndex,
        IReadOnlyDictionary<string, string?> DefaultLevelById,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> MessageStringsById,
        IReadOnlyDictionary<string, string> GlobalMessageStrings,
        IReadOnlyDictionary<string, (string? Uri, string? BaseId)> UriBases);

    private static RunInfo ReadRunInfo(JsonElement run)
    {
        var ids = new List<string>();
        var levels = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var messageStrings = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var globalStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var uriBases = new Dictionary<string, (string?, string?)>(StringComparer.OrdinalIgnoreCase);

        if (TryGetObjectProperty(run, "tool", out var tool)
            && TryGetObjectProperty(tool, "driver", out var driver))
        {
            if (driver.TryGetProperty("globalMessageStrings", out var globals))
                ReadMessageStrings(globals, globalStrings);

            if (driver.TryGetProperty("rules", out var rules)
                && rules.ValueKind == JsonValueKind.Array)
            {
                foreach (var rule in rules.EnumerateArray())
                {
                    if (rule.ValueKind != JsonValueKind.Object)
                        throw new FormatException(
                            "Invalid SARIF log: a rule descriptor is not an object.");
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

        if (run.TryGetProperty("originalUriBaseIds", out var bases)
            && bases.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in bases.EnumerateObject())
            {
                uriBases[entry.Name] = (
                    GetString(entry.Value, "uri"),
                    GetString(entry.Value, "uriBaseId"));
            }
        }

        return new RunInfo(ids, levels, messageStrings, globalStrings, uriBases);
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

    private static BuildDiagnostic ConvertResult(JsonElement result, RunInfo run, string? baseDirectory)
    {
        var ruleId = GetString(result, "ruleId")
            ?? (result.TryGetProperty("rule", out var ruleRef) ? GetString(ruleRef, "id") : null)
            ?? RuleIdAtIndex(result, run)
            ?? "unknown";

        // Severity: the result's own level wins, then the rule descriptor's
        // default level, then the SARIF default — warning for a failed
        // check, none for the other (informational/review) kinds. The
        // note/none levels are preserved, not promoted to warning — they
        // are the whole reason this channel exists.
        var kind = GetString(result, "kind");
        var level = GetString(result, "level")
            ?? (run.DefaultLevelById.TryGetValue(ruleId, out var defaultLevel) ? defaultLevel : null)
            ?? (kind is null or "fail" ? "warning" : "none");

        var message = ReadMessage(result, ruleId, run);

        string? file = null;
        var line = 0;
        var column = 0;
        if (result.TryGetProperty("locations", out var locations)
            && locations.ValueKind == JsonValueKind.Array
            && locations.GetArrayLength() > 0
            && TryGetObjectProperty(locations[0], "physicalLocation", out var physical))
        {
            if (TryGetObjectProperty(physical, "artifactLocation", out var artifact))
                file = ResolveArtifactUri(artifact, run, baseDirectory);
            if (TryGetObjectProperty(physical, "region", out var region))
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

    private static string? RuleIdAtIndex(JsonElement result, RunInfo run)
    {
        if (result.TryGetProperty("ruleIndex", out var index)
            && index.ValueKind == JsonValueKind.Number
            && index.TryGetInt32(out var i)
            && i >= 0 && i < run.IdsByIndex.Count
            && run.IdsByIndex[i].Length > 0)
        {
            return run.IdsByIndex[i];
        }
        return null;
    }

    private static string ReadMessage(JsonElement result, string ruleId, RunInfo run)
    {
        // SARIF requires every result to carry a message whose text can be
        // resolved. A missing message or an unresolved message id is an
        // invalid log, not a finding with nothing to say.
        if (!result.TryGetProperty("message", out var message)
            || message.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException(
                $"Invalid SARIF log: a result for rule '{ruleId}' has no message.");
        }

        var text = GetString(message, "text");
        if (text == null)
        {
            // No inline text: resolve the message id against the rule's
            // messageStrings, then the driver's globalMessageStrings.
            var id = GetString(message, "id");
            if (id != null)
            {
                if (run.MessageStringsById.TryGetValue(ruleId, out var perRule)
                    && perRule.TryGetValue(id, out var fromRule))
                {
                    text = fromRule;
                }
                else if (run.GlobalMessageStrings.TryGetValue(id, out var fromGlobal))
                {
                    text = fromGlobal;
                }
            }
            if (text == null)
            {
                throw new FormatException(
                    $"Invalid SARIF log: the message of a result for rule '{ruleId}' " +
                    "has no text and its id (if any) resolves to no message string.");
            }
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
    /// Turns an artifactLocation into a filesystem path. A <c>file://</c>
    /// or rooted URI stands on its own; a relative URI resolves against
    /// its <c>uriBaseId</c> (through the run's
    /// <c>originalUriBaseIds</c>) when it has one, and against the caller's
    /// base directory otherwise.
    /// </summary>
    private static string? ResolveArtifactUri(JsonElement artifact, RunInfo run, string? baseDirectory)
    {
        var uri = GetString(artifact, "uri");
        var baseId = GetString(artifact, "uriBaseId");

        if (uri != null
            && uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(uri, UriKind.Absolute, out var fileUri))
        {
            return fileUri.LocalPath;
        }
        if (uri != null && Path.IsPathRooted(Uri.UnescapeDataString(uri)))
            return Uri.UnescapeDataString(uri);
        if (uri == null && baseId == null)
            return null;

        var basePath = baseId != null
            ? ResolveUriBase(baseId, run, baseDirectory, depth: 0) ?? baseDirectory
            : baseDirectory;
        var relative = uri != null ? Uri.UnescapeDataString(uri) : string.Empty;
        if (basePath == null)
            return relative.Length > 0 ? relative : null;
        return relative.Length > 0
            ? Path.GetFullPath(Path.Combine(basePath, relative))
            : basePath;
    }

    /// <summary>
    /// Resolves a <c>uriBaseId</c> through the run's
    /// <c>originalUriBaseIds</c>; a base's own URI may itself be relative
    /// (resolved against its own base, or the caller's directory) or a
    /// <c>file://</c> URI. Unknown ids resolve to null, leaving the caller
    /// to fall back.
    /// </summary>
    private static string? ResolveUriBase(string id, RunInfo run, string? baseDirectory, int depth)
    {
        if (depth > 8)
            return null;
        if (!run.UriBases.TryGetValue(id, out var entry))
            return null;
        var (uri, baseId) = entry;

        var parent = baseId != null
            ? ResolveUriBase(baseId, run, baseDirectory, depth + 1)
            : baseDirectory;
        if (uri == null)
            return parent;
        if (uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(uri, UriKind.Absolute, out var fileUri))
        {
            return fileUri.LocalPath;
        }
        var path = Uri.UnescapeDataString(uri);
        if (Path.IsPathRooted(path))
            return path;
        return parent != null ? Path.GetFullPath(Path.Combine(parent, path)) : path;
    }

    /// <summary>
    /// Property lookup that is safe on any element: a non-object element
    /// simply has no properties. (<see cref="JsonElement.TryGetProperty"/>
    /// itself throws <see cref="InvalidOperationException"/> on non-object
    /// elements; malformed SARIF must surface as
    /// <see cref="FormatException"/> per this reader's contract.)
    /// </summary>
    private static bool TryGetObjectProperty(JsonElement element, string property, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out value);
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
