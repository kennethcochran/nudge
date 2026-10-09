namespace Nudge;

/// <summary>
/// Baseline (snooze) support: a text file of accepted findings, one per line,
/// in the form <c>RULEID|relative/path/to/File.cs</c>. Findings matching an
/// entry are skipped, so only new diagnostics surface. Line numbers are
/// deliberately not part of the fingerprint — code motion within a file
/// must not resurrect a snoozed finding.
/// </summary>
public static class Baseline
{
    /// <summary>Loads baseline entries; missing file means an empty baseline.</summary>
    public static HashSet<string> Load(string? path)
    {
        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (path == null || !File.Exists(path))
            return entries;

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            entries.Add(Normalize(line));
        }
        return entries;
    }

    /// <summary>Writes current findings as a baseline file.</summary>
    public static void Write(string path, IEnumerable<BuildDiagnostic> diagnostics, string rootDir)
    {
        var lines = new List<string>
        {
            "# nudge baseline — findings accepted as of " + DateTime.UtcNow.ToString("yyyy-MM-dd"),
            "# Format: RULEID|path/relative/to/repo-root",
            "# Delete lines to unsnooze; findings in files you touch should be reviewed, not blindly re-snoozed.",
        };
        foreach (var d in diagnostics.OrderBy(d => d.RuleId).ThenBy(d => d.File))
        {
            var rel = d.File != null ? MakeRelative(d.File, rootDir) : "(no file)";
            lines.Add($"{d.RuleId}|{rel}");
        }
        File.WriteAllLines(path, lines);
    }

    /// <summary>True when the diagnostic is covered by the baseline.</summary>
    public static bool IsSnoozed(BuildDiagnostic diagnostic, HashSet<string> entries, string rootDir)
    {
        if (diagnostic.File == null)
            return entries.Contains(Normalize($"{diagnostic.RuleId}|(no file)"));
        return entries.Contains(Normalize($"{diagnostic.RuleId}|{MakeRelative(diagnostic.File, rootDir)}"));
    }

    private static string MakeRelative(string file, string rootDir)
    {
        var rel = Path.GetRelativePath(rootDir, file);
        return rel.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string Normalize(string entry) => entry.Replace('\\', '/').Trim();
}
