using System.Diagnostics;
using Microsoft.Build.Framework;
using Nudge;

namespace Nudge.Logger;

/// <summary>
/// MSBuild logger that puts nudge coaching directly into the build output,
/// next to the diagnostic it belongs to. Loaded via
/// <c>/logger:Nudge.Logger.NudgeLogger,&lt;path&gt;/Nudge.Logger.dll</c>,
/// typically through a <c>Directory.Build.rsp</c> written by <c>nudge init</c>.
///
/// Parameters (semicolon-separated, <c>key=value</c>):
/// <list type="bullet">
/// <item><c>baseline</c> — path to the nudge baseline file. Defaults to
/// <c>.nudge/baseline.txt</c>. May be absolute (e.g. via
/// <c>%MSBuildThisFileDirectory%</c> in the .rsp) or relative to the build's
/// working directory. Baselined (snoozed) findings get no coaching.</item>
/// <item><c>sarifdir</c> — directory holding the per-project ErrorLog
/// SARIF files read at build end. Defaults to <c>.nudge/sarif</c> under the
/// repo root (derived from the baseline path).</item>
/// <item><c>console</c> — when <c>true</c>, the logger also acts as the
/// build's console logger (install with <c>nudge init --console-logger</c>,
/// which adds <c>/noconsolelogger</c>): it prints every diagnostic itself in
/// canonical MSBuild format, with coaching inline, plus the end-of-build
/// summary. Without it, the default console logger prints diagnostics as
/// usual and this logger only adds coaching lines.</item>
/// </list>
///
/// Coaching prints at diagnostic time, not in a report the reader has to go
/// find afterwards: the first occurrence of a rule in a build gets the full
/// guide (Why / Do this / AVOID); repeats of the same rule get a one-line
/// reminder. Diagnostics with no recorded guide print nothing extra.
///
/// Info-severity findings never reach the event stream at all, so they are
/// coached from a second channel: at build end the logger reads the
/// per-project ErrorLog SARIF files (arranged by <c>nudge init</c> in
/// <c>Directory.Build.props</c>) and coaches the note/none findings in
/// them. The partition is disjoint by construction — events own
/// warning/error, the SARIF pass owns note/none.
///
/// The logger never fails the build: coaching, not gating. Any internal
/// failure is swallowed with a one-line notice so a broken logger can never
/// break a build.
/// </summary>
public sealed class NudgeLogger : ILogger
{
    public LoggerVerbosity Verbosity { get; set; } = LoggerVerbosity.Normal;

    public string Parameters { get; set; } = string.Empty;

    private readonly object _gate = new();
    private readonly List<BuildDiagnostic> _diagnostics = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly HashSet<string> _coachedRules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stopwatch _elapsed = new();

    private bool _setupDone;
    private bool _consoleMode;
    private bool _summaryPrinted;
    private bool _sarifFinishDone;
    private bool _failureNoted;
    private HashSet<string> _baseline = new(StringComparer.OrdinalIgnoreCase);
    private string _rootDir = Directory.GetCurrentDirectory();
    private string? _sarifDir;
    private RuleCatalog? _catalog;

    public void Initialize(IEventSource eventSource)
    {
        _elapsed.Start();
        eventSource.ErrorRaised += (_, e) =>
            OnDiagnostic(e.Code, e.File, e.LineNumber, e.ColumnNumber, e.Message, e.ProjectFile, "error");
        eventSource.WarningRaised += (_, e) =>
            OnDiagnostic(e.Code, e.File, e.LineNumber, e.ColumnNumber, e.Message, e.ProjectFile, "warning");
        eventSource.MessageRaised += (_, e) =>
        {
            // In console mode we are the only mouthpiece; high-importance
            // messages (restore results, SDK notices) must not vanish.
            if (_consoleMode && e.Importance == MessageImportance.High && !string.IsNullOrEmpty(e.Message))
                WriteLines(e.Message);
        };
        eventSource.BuildFinished += (_, e) => OnBuildFinished(e.Succeeded);
    }

    public void Shutdown()
    {
        // Coaching is printed at diagnostic time; nothing to flush here.
    }

    private void OnDiagnostic(
        string? code, string? file, int line, int column,
        string? message, string? projectFile, string severity)
    {
        if (string.IsNullOrEmpty(code))
            return;

        lock (_gate)
        {
            try
            {
                EnsureSetup();

                // Same dedupe key as the text log parser: multi-targeted
                // builds emit the same diagnostic once per target framework.
                var key = $"{code}|{file}|{line}|{message}";
                if (!_seen.Add(key))
                    return;

                var diagnostic = new BuildDiagnostic(
                    string.IsNullOrEmpty(file) ? null : file,
                    line,
                    column,
                    severity,
                    code,
                    message ?? string.Empty,
                    string.IsNullOrEmpty(projectFile) ? null : projectFile);
                _diagnostics.Add(diagnostic);

                if (_consoleMode)
                    WriteLines(FormatDiagnostic(diagnostic));

                if (Baseline.IsSnoozed(diagnostic, _baseline, _rootDir))
                    return;
                if (_catalog == null || _catalog.UsesFallback(code))
                    return;

                if (!_coachedRules.Add(code))
                {
                    WriteLines($"nudge: {code} — coaching printed with its first occurrence in this build.");
                    return;
                }

                var guide = _catalog.Resolve(code, message ?? string.Empty);
                WriteLines(RenderCoaching(guide));
            }
            catch (Exception ex)
            {
                NoteFailure(ex);
            }
        }
    }

    private void OnBuildFinished(bool succeeded)
    {
        lock (_gate)
        {
            try
            {
                EnsureSetup();

                // The SARIF finish runs in both modes, exactly once: it is
                // the only channel that can coach Info-severity findings.
                if (!_sarifFinishDone)
                {
                    _sarifFinishDone = true;
                    CoachSarifFindings();
                }

                if (!_consoleMode || _summaryPrinted)
                    return;
                _summaryPrinted = true;

                // Mirror the default console logger's summary: repeat the
                // diagnostics, then counts, then elapsed time.
                var lines = new List<string> { string.Empty, succeeded ? "Build succeeded." : "Build FAILED.", string.Empty };
                foreach (var d in _diagnostics)
                    lines.Add(FormatDiagnostic(d));
                if (_diagnostics.Count > 0)
                    lines.Add(string.Empty);
                lines.Add($"    {_diagnostics.Count(d => !d.IsError)} Warning(s)");
                lines.Add($"    {_diagnostics.Count(d => d.IsError)} Error(s)");
                lines.Add(string.Empty);
                lines.Add($"Time Elapsed {_elapsed.Elapsed:hh\\:mm\\:ss\\.ff}");
                WriteLines(lines.ToArray());
            }
            catch (Exception ex)
            {
                NoteFailure(ex);
            }
        }
    }

    /// <summary>
    /// Coaches the note/none findings in the per-project ErrorLog SARIF
    /// files under <see cref="_sarifDir"/>. Warning/error results in those
    /// files are ignored — the event stream owns them, and coaching both
    /// channels would double-print. Each surviving finding prints its
    /// diagnostic line (it never appeared in the console output) followed
    /// by the same coaching the event path gives: full guide on the rule's
    /// first occurrence in the build, one-line reminder after.
    /// </summary>
    private void CoachSarifFindings()
    {
        if (_sarifDir == null || _catalog == null || !Directory.Exists(_sarifDir))
            return;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unreadable = 0;
        foreach (var file in Directory.EnumerateFiles(_sarifDir, "*.sarif")
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            IReadOnlyList<BuildDiagnostic> results;
            try
            {
                results = SarifReader.ReadFile(file, _rootDir);
            }
            catch (Exception)
            {
                // One bad file must not lose the others; it is reported
                // once, below, rather than silently dropped.
                unreadable++;
                continue;
            }

            foreach (var diagnostic in results)
            {
                if (!diagnostic.Severity.Equals("note", StringComparison.OrdinalIgnoreCase)
                    && !diagnostic.Severity.Equals("none", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Same dedupe key as the event path: multi-targeted
                // projects repeat a finding across their SARIF runs.
                var key = $"{diagnostic.RuleId}|{diagnostic.File}|{diagnostic.Line}|{diagnostic.Message}";
                if (!seen.Add(key))
                    continue;

                if (Baseline.IsSnoozed(diagnostic, _baseline, _rootDir))
                    continue;
                if (_catalog.UsesFallback(diagnostic.RuleId))
                    continue;

                WriteLines(FormatDiagnostic(diagnostic));
                if (!_coachedRules.Add(diagnostic.RuleId))
                {
                    WriteLines($"nudge: {diagnostic.RuleId} — coaching printed with its first occurrence in this build.");
                    continue;
                }

                var guide = _catalog.Resolve(diagnostic.RuleId, diagnostic.Message);
                WriteLines(RenderCoaching(guide));
            }
        }

        if (unreadable > 0)
            WriteLines($"nudge: {unreadable} SARIF file(s) under {_sarifDir} could not be read; their findings were not coached.");
    }

    private static string FormatDiagnostic(BuildDiagnostic d)
    {
        var project = d.Project != null ? $" [{d.Project}]" : string.Empty;
        return d.File != null
            ? $"{d.File}({d.Line},{d.Column}): {d.Severity} {d.RuleId}: {d.Message}{project}"
            : $"{d.Severity} {d.RuleId}: {d.Message}{project}";
    }

    private static string[] RenderCoaching(RuleGuide guide)
    {
        var title = guide.Title;
        if (title.StartsWith(guide.RuleId + " — ", StringComparison.OrdinalIgnoreCase))
            title = title[(guide.RuleId.Length + 3)..];

        var lines = new List<string>
        {
            $"nudge: coaching for {guide.RuleId} — {title}",
            $"nudge: Why: {Collapse(guide.Why)}",
            "nudge: Do this:",
        };
        for (var i = 0; i < guide.DoThis.Count; i++)
            lines.Add($"nudge:   {i + 1}. {guide.DoThis[i]}");
        lines.Add("nudge: AVOID:");
        foreach (var item in guide.Avoid)
            lines.Add($"nudge:   - {item}");
        return lines.ToArray();
    }

    private static string Collapse(string text) =>
        string.Join(' ', text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())).Trim();

    private void EnsureSetup()
    {
        if (_setupDone)
            return;
        _setupDone = true;

        var parameters = ParseParameters(Parameters);
        _consoleMode = parameters.GetValueOrDefault("console", "false")
            .Equals("true", StringComparison.OrdinalIgnoreCase);

        var baselinePath = Path.GetFullPath(parameters.GetValueOrDefault(
            "baseline", Path.Combine(".nudge", "baseline.txt")));
        var baselineDir = Path.GetDirectoryName(baselinePath)
            ?? Directory.GetCurrentDirectory();
        // Baseline entries are relative to the repo root; the baseline
        // lives in <root>/.nudge/, so the root is its parent.
        _rootDir = Path.GetFileName(baselineDir).Equals(
                ".nudge", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(baselineDir) ?? baselineDir
            : baselineDir;

        _sarifDir = Path.GetFullPath(parameters.GetValueOrDefault(
            "sarifdir", Path.Combine(_rootDir, ".nudge", "sarif")));

        _baseline = Baseline.Load(baselinePath);
        var rulesDir = Directory.Exists(Path.Combine(
            Directory.GetCurrentDirectory(), "rules"))
            ? Path.Combine(Directory.GetCurrentDirectory(), "rules")
            : null;
        _catalog = RuleCatalog.WithInstalledRulesets(rulesDir);
    }

    private void WriteLines(params string[] lines)
    {
        foreach (var line in lines)
            Console.WriteLine(line);
    }

    private void NoteFailure(Exception ex)
    {
        // A broken logger must never break the build it observes; say so
        // once, then stay silent.
        if (_failureNoted)
            return;
        _failureNoted = true;
        Console.WriteLine($"nudge: coaching skipped ({ex.Message})");
    }

    private static Dictionary<string, string> ParseParameters(string parameters)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in parameters.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;
            var value = part.Substring(eq + 1).Trim();
            // MSBuild normally strips the quotes `nudge init` writes around
            // paths in Directory.Build.rsp; a host that passes them through
            // must not turn the quoted path into a literal filename.
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                value = value[1..^1];
            result[part.Substring(0, eq).Trim()] = value;
        }
        return result;
    }
}
