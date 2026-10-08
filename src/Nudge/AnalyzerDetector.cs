using System.Text.RegularExpressions;

namespace Nudge;

/// <summary>
/// Detects which static analysis tools a project uses by scanning
/// PackageReference entries in its .csproj files, then maps them to
/// the Nudge rulesets that carry their coaching guides.
/// </summary>
internal static class AnalyzerDetector
{
    /// <summary>Analyzer NuGet package ID → ruleset name.</summary>
    public static readonly IReadOnlyDictionary<string, string> AnalyzerToRuleset =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SonarAnalyzer.CSharp"] = "sonar",
            ["Microsoft.CodeAnalysis.NetAnalyzers"] = "roslyn",
            ["StyleCop.Analyzers"] = "stylecop",
            ["Meziantou.Analyzer"] = "meziantou",
            ["Roslynator.Analyzers"] = "roslynator",
        };

    private static readonly Regex PackageRef =
        new("<PackageReference\\s+Include\\s*=\\s*\"([^\"]+)\"",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Scans *.csproj files under rootDir for known analyzer packages.
    /// Returns the analyzer package IDs found.
    /// </summary>
    public static IReadOnlyList<string> DetectAnalyzers(string rootDir)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> projects;
        try
        {
            projects = Directory.GetFiles(rootDir, "*.csproj", SearchOption.AllDirectories);
        }
        catch
        {
            return Array.Empty<string>();
        }

        foreach (var project in projects)
        {
            string text;
            try { text = File.ReadAllText(project); }
            catch { continue; }
            foreach (Match m in PackageRef.Matches(text))
            {
                var id = m.Groups[1].Value;
                if (AnalyzerToRuleset.ContainsKey(id))
                    found.Add(id);
            }
        }
        return found.OrderBy(x => x).ToList();
    }

    /// <summary>
    /// Analyzers detected in the project whose rulesets are not installed.
    /// Returns (analyzerPackageId, rulesetName) pairs.
    /// </summary>
    public static IReadOnlyList<(string Analyzer, string Ruleset)> MissingRulesets(string rootDir)
    {
        return DetectAnalyzers(rootDir)
            .Where(a => !RulesetManager.IsInstalled(AnalyzerToRuleset[a]))
            .Select(a => (a, AnalyzerToRuleset[a]))
            .ToList();
    }
}
