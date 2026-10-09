namespace Nudge;

/// <summary>
/// The shared diagnostic→coaching pipeline: sort, baseline-filter, group by
/// rule, resolve guides, render the report. Consumed by the CLI text path and
/// by the MSBuild logger — the logger feeds it structured diagnostics straight
/// from build events instead of parsed log text.
/// </summary>
public static class CoachingEngine
{
    public sealed record Result(
        string Report,
        int FindingCount,
        bool HasErrors,
        IReadOnlyList<string> FallbackRuleIds);

    public static Result Generate(
        IEnumerable<BuildDiagnostic> diagnostics,
        RuleCatalog catalog,
        HashSet<string> baseline,
        string rootDir,
        string scopeLabel,
        bool buildFailed)
    {
        var findings = diagnostics
            .OrderBy(d => d.RuleId)
            .ThenBy(d => d.File)
            .ThenBy(d => d.Line)
            .Where(d => !Baseline.IsSnoozed(d, baseline, rootDir))
            .ToList();

        if (findings.Count == 0)
            return new Result(Reporter.RenderClean(scopeLabel), 0, false, Array.Empty<string>());

        var groups = findings
            .GroupBy(d => d.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var report = Reporter.Render(groups, catalog, rootDir, scopeLabel, buildFailed);
        var fallbackRuleIds = groups
            .Select(g => g.Key)
            .Where(ruleId => catalog.UsesFallback(ruleId))
            .ToList();
        return new Result(report, findings.Count, findings.Any(d => d.IsError), fallbackRuleIds);
    }
}
