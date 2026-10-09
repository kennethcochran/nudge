using Microsoft.Build.Framework;
using Nudge;

namespace Nudge.Logger;

/// <summary>
/// MSBuild logger that turns build diagnostics into nudge coaching guides.
/// Loaded via <c>/logger:Nudge.Logger.NudgeLogger,&lt;path&gt;/Nudge.Logger.dll</c>,
/// typically through a <c>Directory.Build.rsp</c> written by <c>nudge init</c>.
///
/// Parameters (semicolon-separated, <c>key=value</c>):
/// <list type="bullet">
/// <item><c>baseline</c> — path to the nudge baseline file. Defaults to
/// <c>.nudge/baseline.txt</c>. May be absolute (e.g. via
/// <c>%MSBuildThisFileDirectory%</c> in the .rsp) or relative to the build's
/// working directory.</item>
/// </list>
///
/// At build end the full coaching report is written next to the baseline file
/// as <c>report.md</c>, and a one-line pointer goes to the console. The logger
/// never fails the build: coaching, not gating. Any internal failure is
/// swallowed with a one-line notice so a broken logger can never break a build.
/// </summary>
public sealed class NudgeLogger : ILogger
{
    public LoggerVerbosity Verbosity { get; set; } = LoggerVerbosity.Normal;

    public string Parameters { get; set; } = string.Empty;

    private readonly List<BuildDiagnostic> _diagnostics = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private bool _finished;

    public void Initialize(IEventSource eventSource)
    {
        eventSource.ErrorRaised += (_, e) =>
            Add(e.Code, e.File, e.LineNumber, e.ColumnNumber, e.Message, e.ProjectFile, "error");
        eventSource.WarningRaised += (_, e) =>
            Add(e.Code, e.File, e.LineNumber, e.ColumnNumber, e.Message, e.ProjectFile, "warning");
        eventSource.BuildFinished += (_, _) => Finish();
    }

    public void Shutdown()
    {
        // Report is produced on BuildFinished; nothing to flush here.
    }

    private void Add(
        string? code, string? file, int line, int column,
        string? message, string? projectFile, string severity)
    {
        if (string.IsNullOrEmpty(code))
            return;

        // Same dedupe key as the text log parser: multi-targeted builds emit
        // the same diagnostic once per target framework.
        var key = $"{code}|{file}|{line}|{message}";
        if (!_seen.Add(key))
            return;

        _diagnostics.Add(new BuildDiagnostic(
            string.IsNullOrEmpty(file) ? null : file,
            line,
            column,
            severity,
            code,
            message ?? string.Empty,
            string.IsNullOrEmpty(projectFile) ? null : projectFile));
    }

    private void Finish()
    {
        if (_finished)
            return;
        _finished = true;

        try
        {
            var parameters = ParseParameters(Parameters);
            var baselinePath = Path.GetFullPath(parameters.GetValueOrDefault(
                "baseline", Path.Combine(".nudge", "baseline.txt")));
            var baselineDir = Path.GetDirectoryName(baselinePath)
                ?? Directory.GetCurrentDirectory();
            // Baseline entries are relative to the repo root; the baseline
            // lives in <root>/.nudge/, so the root is its parent.
            var rootDir = Path.GetFileName(baselineDir).Equals(
                    ".nudge", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(baselineDir) ?? baselineDir
                : baselineDir;

            var baseline = Baseline.Load(baselinePath);
            var rulesDir = Directory.Exists(Path.Combine(
                Directory.GetCurrentDirectory(), "rules"))
                ? Path.Combine(Directory.GetCurrentDirectory(), "rules")
                : null;
            var catalog = RuleCatalog.WithInstalledRulesets(rulesDir);

            var result = CoachingEngine.Generate(
                _diagnostics, catalog, baseline, rootDir, "msbuild",
                buildFailed: false);

            var reportPath = Path.Combine(baselineDir, "report.md");
            File.WriteAllText(reportPath, result.Report);

            Console.WriteLine(result.FindingCount == 0
                ? $"nudge: clean — no findings."
                : $"nudge: {result.FindingCount} finding(s) across {result.RuleCount} rule(s) — see {reportPath}");
        }
        catch (Exception ex)
        {
            // A broken logger must never break the build it observes.
            Console.WriteLine($"nudge: coaching skipped ({ex.Message})");
        }
    }

    private static Dictionary<string, string> ParseParameters(string parameters)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in parameters.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;
            result[part.Substring(0, eq).Trim()] = part.Substring(eq + 1).Trim();
        }
        return result;
    }
}
