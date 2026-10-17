namespace Nudge;

/// <summary>
/// A coaching guide for one diagnostic rule: the "why" behind the rule,
/// concrete remediation steps, and the metric-gaming anti-patterns to avoid.
/// This is the Habit Hooks pattern — the linter finding is the cue,
/// the guide is the action.
/// </summary>
public sealed record RuleGuide(
    string RuleId,
    string Title,
    string Why,
    IReadOnlyList<string> DoThis,
    IReadOnlyList<string> Avoid);

/// <summary>
/// Rule metadata for your own custom analyzers' <c>DiagnosticDescriptor</c>
/// declarations (title + description), used when no hand-written guide exists
/// for a rule ID. Add entries here for project-specific analyzers; the
/// shipped <c>rules/</c> directory already covers the SonarSource corpus.
/// </summary>
public static class KnownRules
{
    public static readonly IReadOnlyDictionary<string, (string Title, string Description)> Descriptors =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            // Example:
            // ["MY001"] = (
            //     "Short Title",
            //     "One or two sentences describing what the rule protects against."),
        };
}

/// <summary>
/// Resolves a <see cref="RuleGuide"/> for a diagnostic rule ID.
/// Resolution order:
/// <list type="number">
/// <item>A hand-written coaching guide at <c>&lt;rulesDir&gt;/&lt;RULEID&gt;.md</c> (highest priority —
/// this is where project-specific taste lives). Multiple directories are searched in order.</item>
/// <item>Metadata harvested from the project's own analyzer descriptors,
/// wrapped in the generic coaching template.</item>
/// <item>A generic fallback guide that still carries the anti-gaming instruction.</item>
/// </list>
/// </summary>
public sealed class RuleCatalog
{
    private readonly IReadOnlyList<string> _rulesDirs;

    public RuleCatalog(string? rulesDir) : this(
        rulesDir != null ? new[] { rulesDir } : Array.Empty<string>())
    {
    }

    public RuleCatalog(IEnumerable<string> rulesDirs)
    {
        _rulesDirs = rulesDirs.Where(Directory.Exists).ToList();
    }

    /// <summary>
    /// Builds the default catalog: the explicit/project rules dir (if any),
    /// then every installed ruleset under ~/.nudge/rulesets/.
    /// </summary>
    public static RuleCatalog WithInstalledRulesets(string? rulesDir)
    {
        var dirs = new List<string>();
        if (rulesDir != null)
            dirs.Add(rulesDir);
        foreach (var (_, manifest) in RulesetManager.ListInstalled())
        {
            var guidesDir = RulesetManager.GuidesDir(manifest.Name);
            if (guidesDir != null)
                dirs.Add(guidesDir);
        }
        return new RuleCatalog(dirs);
    }

    public RuleGuide Resolve(string ruleId, string rawMessage)
    {
        foreach (var rulesDir in _rulesDirs)
        {
            var guide = TryLoadGuide(rulesDir, ruleId);
            if (guide != null)
                return guide;
        }
        if (KnownRules.Descriptors.TryGetValue(ruleId, out var known))
        {
            return new RuleGuide(
                ruleId,
                known.Title,
                known.Description,
                new[]
                {
                    "Fix the underlying design issue the rule describes, in the flagged code itself.",
                    "Re-run the build and confirm the diagnostic is gone because the code changed, not because the rule was silenced.",
                },
                new[]
                {
                    "Do not suppress this with #pragma warning disable, [SuppressMessage], or .editorconfig severity overrides — suppression is forbidden by the project's AGENTS.md except in ANTLR-generated code.",
                    "Do not restructure the code merely to dodge the detector while keeping the underlying problem (e.g. wrapping the flagged construct in a helper that hides it).",
                });
        }

        return FallbackGuide(ruleId, rawMessage);
    }

    /// <summary>
    /// True when resolving this rule ID would fall through to the generic
    /// fallback guide (no guide file in any rules dir, no KnownRules entry).
    /// Uses the same case-insensitive lookup as <see cref="Resolve"/> so the
    /// two can never disagree about whether a guide exists.
    /// </summary>
    public bool UsesFallback(string ruleId)
    {
        foreach (var rulesDir in _rulesDirs)
        {
            if (FindGuideFile(rulesDir, ruleId) != null)
                return false;
        }
        return !KnownRules.Descriptors.ContainsKey(ruleId);
    }

    /// <summary>
    /// Finds the guide file for <paramref name="ruleId"/> in
    /// <paramref name="rulesDir"/>, matching the file name case-insensitively:
    /// rule IDs arrive in varying case and Linux filesystems are
    /// case-sensitive, so "s1234" must find "S1234.md".
    /// </summary>
    private static string? FindGuideFile(string rulesDir, string ruleId)
    {
        var direct = Path.Combine(rulesDir, ruleId + ".md");
        if (File.Exists(direct))
            return direct;

        return Directory.EnumerateFiles(rulesDir, "*.md")
            .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                .Equals(ruleId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Loads the guide for <paramref name="ruleId"/> from <paramref name="rulesDir"/>.
    /// </summary>
    private static RuleGuide? TryLoadGuide(string rulesDir, string ruleId)
    {
        var match = FindGuideFile(rulesDir, ruleId);
        return match != null ? ParseGuide(match, ruleId) : null;
    }

    private static RuleGuide ParseGuide(string path, string ruleId)
    {
        var text = File.ReadAllText(path);
        var title = FirstHeadingTitle(text) ?? ruleId;
        var why = ExtractSection(text, "Why") ?? "No rationale recorded for this rule yet.";
        var doThis = ExtractList(ExtractSection(text, "Do this") ?? string.Empty);
        var avoid = ExtractList(ExtractSection(text, "AVOID") ?? ExtractSection(text, "Avoid") ?? string.Empty);

        if (doThis.Count == 0)
            doThis = new[] { "Address the underlying issue described above, then rebuild to confirm the diagnostic is resolved by the code change." };
        if (avoid.Count == 0)
            avoid = new[] { "Do not suppress or game the diagnostic — fix the root cause." };

        return new RuleGuide(ruleId, title, why, doThis, avoid);
    }

    private static RuleGuide FallbackGuide(string ruleId, string rawMessage) =>
        new(
            ruleId,
            $"Diagnostic {ruleId}",
            (string.IsNullOrWhiteSpace(rawMessage) ? "" : $"The build reported: {rawMessage} ") +
            "No coaching guide is recorded for this rule yet. " +
            "If this rule fires often, add a guide at rules/" + ruleId + ".md explaining why the rule matters and what a good fix looks like.",
            new[]
            {
                "Investigate what the rule is protecting against and fix the root cause in the flagged code.",
                "Re-run the build and confirm the diagnostic is gone because the code changed, not because the rule was silenced.",
            },
            new[]
            {
                "Do not suppress this with #pragma warning disable, [SuppressMessage], or .editorconfig severity overrides unless the project's AGENTS.md explicitly allows it for this case.",
                "Do not make cosmetic edits whose only effect is to silence the detector while the underlying issue remains.",
            });

    private static string? FirstHeadingTitle(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
                return trimmed[2..].Trim();
        }
        return null;
    }

    private static string? ExtractSection(string text, string heading)
    {
        var lines = text.Split('\n');
        var start = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim().TrimEnd(':');
            if (trimmed.Equals("## " + heading, StringComparison.OrdinalIgnoreCase))
            {
                start = i + 1;
                break;
            }
        }
        if (start < 0)
            return null;

        var end = lines.Length;
        for (int i = start; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("## ", StringComparison.Ordinal))
            {
                end = i;
                break;
            }
        }

        var section = string.Join("\n", lines[start..end]).Trim();
        return string.IsNullOrEmpty(section) ? null : section;
    }

    private static readonly System.Text.RegularExpressions.Regex NumberedItem =
        new(@"^\d+\.\s+(.*)$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static IReadOnlyList<string> ExtractList(string section)
    {
        var items = new List<string>();
        var current = new System.Text.StringBuilder();
        void Flush()
        {
            if (current.Length > 0)
            {
                items.Add(current.ToString().Trim());
                current.Clear();
            }
        }

        foreach (var line in section.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                Flush();
                current.Append(trimmed[2..].Trim());
                continue;
            }
            var m = NumberedItem.Match(trimmed);
            if (m.Success)
            {
                Flush();
                current.Append(m.Groups[1].Value.Trim());
                continue;
            }
            // Continuation line: indented text belonging to the current item.
            if (current.Length > 0 && trimmed.Length > 0 &&
                (line.StartsWith(' ') || line.StartsWith('\t')))
            {
                current.Append(' ');
                current.Append(trimmed);
            }
        }
        Flush();
        return items;
    }
}
