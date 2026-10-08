using System.Text;

namespace Nudge;

/// <summary>
/// Renders the coaching report in the Habit Hooks shape:
/// one section per rule — why it matters, what to do, what to avoid —
/// followed by the concrete findings. Written for an AI agent reader:
/// the rationale comes before the file list so the fix is shaped by
/// understanding, not by the metric.
/// </summary>
internal static class Reporter
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
            var severity = group.Any(d => d.IsError) ? "error" : "warning";

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

    private static string Shorten(string file, string rootDir)
    {
        var rel = Path.GetRelativePath(rootDir, file);
        return (rel.StartsWith("..") ? file : rel).Replace(Path.DirectorySeparatorChar, '/');
    }
}
