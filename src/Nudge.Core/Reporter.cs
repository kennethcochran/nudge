using System.Text;

namespace Nudge;

/// <summary>
/// Renders the coaching report in the Habit Hooks shape:
/// one section per rule — why it matters, what to do, what to avoid —
/// followed by the concrete findings. Written for an AI agent reader:
/// the rationale comes before the file list so the fix is shaped by
/// understanding, not by the metric.
/// </summary>
public static class Reporter
{
    /// <summary>
    /// Renders findings grouped by rule. <paramref name="rootDir"/> is used
    /// to shorten file paths for readability.
    /// </summary>
    public static string Render(
        IReadOnlyList<IGrouping<string, BuildDiagnostic>> groups,
        RuleCatalog catalog,
        string rootDir,
        string scopeLabel,
        bool buildFailed)
    {
        var sb = new StringBuilder();
        var total = groups.Sum(g => g.Count());

        sb.AppendLine($"# Nudge Report — {scopeLabel}");
        sb.AppendLine();
        if (buildFailed)
        {
            sb.AppendLine("> The build FAILED with errors. Fix the errors below first — warnings may be incomplete until the build succeeds.");
            sb.AppendLine();
        }
        sb.AppendLine($"{total} finding(s) across {groups.Count} rule(s). Fix the underlying issue each rule describes. " +
                      "Do not suppress, silence, or cosmetically dodge the diagnostic — resolve it by changing the code.");
        sb.AppendLine();

        foreach (var group in groups.OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
        {
            var guide = catalog.Resolve(group.Key, group.First().Message);
            var severity = DisplaySeverity(group);

            sb.AppendLine($"--- {guide.RuleId} · {guide.Title} ({group.Count()} {severity} issue(s)) ---");
            sb.AppendLine();
            sb.AppendLine(guide.Why);
            sb.AppendLine();
            sb.AppendLine("**Do this:**");
            sb.AppendLine();
            for (int i = 0; i < guide.DoThis.Count; i++)
                sb.AppendLine($"{i + 1}. {guide.DoThis[i]}");
            sb.AppendLine();
            sb.AppendLine("**AVOID:**");
            sb.AppendLine();
            foreach (var avoid in guide.Avoid)
                sb.AppendLine($"- {avoid}");
            sb.AppendLine();
            foreach (var d in group.OrderBy(d => d.File).ThenBy(d => d.Line))
            {
                var where = d.File != null
                    ? Shorten(d.File, rootDir) + (d.Line > 0 ? $":{d.Line}" : string.Empty)
                    : "(no location)";
                sb.AppendLine($"  {where} — {d.Message}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>Renders the clean report.</summary>
    public static string RenderClean(string scopeLabel) =>
        $"# Nudge Report — {scopeLabel}\n\nClean — no findings.\n";

    /// <summary>
    /// Renders the coaching guide for a single rule, in the same
    /// Why / Do this / AVOID shape as the report sections.
    /// </summary>
    public static string RenderGuide(RuleGuide guide)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# {guide.RuleId} — {guide.Title}");
        sb.AppendLine();
        sb.AppendLine(guide.Why);
        sb.AppendLine();
        sb.AppendLine("**Do this:**");
        sb.AppendLine();
        for (int i = 0; i < guide.DoThis.Count; i++)
            sb.AppendLine($"{i + 1}. {guide.DoThis[i]}");
        sb.AppendLine();
        sb.AppendLine("**AVOID:**");
        sb.AppendLine();
        foreach (var avoid in guide.Avoid)
            sb.AppendLine($"- {avoid}");

        return sb.ToString();
    }

    /// <summary>
    /// The severity word shown for a group: the most severe level present
    /// (error &gt; warning &gt; note &gt; none). Findings that arrive via
    /// SARIF carry the note/none levels the build event stream never
    /// delivers; labelling a pure note group "warning" would misstate it.
    /// </summary>
    private static string DisplaySeverity(IEnumerable<BuildDiagnostic> group)
    {
        var severities = group.Select(d => d.Severity).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (severities.Contains("error")) return "error";
        if (severities.Contains("warning")) return "warning";
        if (severities.Contains("note")) return "note";
        if (severities.Contains("none")) return "none";
        return "warning";
    }

    private static string Shorten(string file, string rootDir)
    {
        var rel = Path.GetRelativePath(rootDir, file);
        return (rel.StartsWith("..") ? file : rel).Replace(Path.DirectorySeparatorChar, '/');
    }
}
