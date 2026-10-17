using System.Text.RegularExpressions;

namespace Nudge;

/// <summary>
/// A single diagnostic (warning or error) parsed from MSBuild output.
/// </summary>
public sealed record BuildDiagnostic(
    string? File,
    int Line,
    int Column,
    string Severity,
    string RuleId,
    string Message,
    string? Project)
{
    /// <summary>True for compiler/analyzer errors (as opposed to warnings).</summary>
    public bool IsError => string.Equals(Severity, "error", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Parses MSBuild-format diagnostic lines from <c>dotnet build</c> output.
/// Handles located diagnostics (<c>File.cs(12,34): warning CS0219: ...</c>)
/// and unlocated ones (<c>warning MSB3644: ...</c>, <c>proj.csproj : warning NU1900: ...</c>).
/// </summary>
public static class LogParser
{
    // e.g. /repo/src/Foo/Bar.cs(12,34): warning S1234: Message text [/repo/src/Foo/Foo.csproj]
    // Tolerates an optional MSBuild "1>" project prefix.
    private static readonly Regex Located = new(
        @"^\s*(?:\d+>\s*)?(?<file>.+?)\((?<line>\d+)(?:,(?<col>\d+))?\):\s*(?<sev>warning|error)\s+(?<rule>[A-Za-z]+\d+[A-Za-z]*):\s*(?<msg>.*?)(?:\s+\[(?<proj>[^\]]+)\])?\s*$",
        RegexOptions.Compiled);

    // e.g. warning MSB3644: ...   or   /repo/src/Foo.csproj : warning NU1900: ...
    private static readonly Regex Unlocated = new(
        @"^\s*(?:\d+>\s*)?(?<file>.*?)\s*:\s*(?<sev>warning|error)\s+(?<rule>[A-Za-z]+\d+[A-Za-z]*):\s*(?<msg>.*?)(?:\s+\[(?<proj>[^\]]+)\])?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Bare form with no file at all: "warning S1234: message"
    private static readonly Regex Bare = new(
        @"^\s*(?:\d+>\s*)?(?<sev>warning|error)\s+(?<rule>[A-Za-z]+\d+[A-Za-z]*):\s*(?<msg>.*?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Parses diagnostics from build log text. Duplicate diagnostics
    /// (same rule, file, line and message — common with multi-targeting) are collapsed.
    /// </summary>
    public static IReadOnlyList<BuildDiagnostic> Parse(string logText)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<BuildDiagnostic>();

        foreach (var rawLine in logText.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var diag = TryParseLine(line);
            if (diag is null)
                continue;

            var key = $"{diag.RuleId}|{diag.File}|{diag.Line}|{diag.Message}";
            if (seen.Add(key))
                result.Add(diag);
        }

        return result;
    }

    private static BuildDiagnostic? TryParseLine(string line)
    {
        var m = Located.Match(line);
        if (m.Success)
        {
            return new BuildDiagnostic(
                m.Groups["file"].Value.Trim(),
                int.Parse(m.Groups["line"].Value),
                m.Groups["col"].Success ? int.Parse(m.Groups["col"].Value) : 0,
                m.Groups["sev"].Value,
                m.Groups["rule"].Value,
                m.Groups["msg"].Value.Trim(),
                m.Groups["proj"].Success ? m.Groups["proj"].Value.Trim() : null);
        }

        m = Unlocated.Match(line);
        if (m.Success)
        {
            var file = m.Groups["file"].Value.Trim();
            return new BuildDiagnostic(
                string.IsNullOrEmpty(file) ? null : file,
                0, 0,
                m.Groups["sev"].Value,
                m.Groups["rule"].Value,
                m.Groups["msg"].Value.Trim(),
                m.Groups["proj"].Success ? m.Groups["proj"].Value.Trim() : null);
        }

        m = Bare.Match(line);
        if (m.Success)
        {
            return new BuildDiagnostic(
                null, 0, 0,
                m.Groups["sev"].Value,
                m.Groups["rule"].Value,
                m.Groups["msg"].Value.Trim(),
                null);
        }

        return null;
    }
}
