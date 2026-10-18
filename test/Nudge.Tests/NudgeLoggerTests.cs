using Microsoft.Build.Framework;
using Nudge.Logger;
using NUnit.Framework;

namespace Nudge.Tests;

/// <summary>
/// Tests for <see cref="NudgeLogger"/> — the MSBuild logger. Drives the
/// logger through the public <see cref="ILogger"/> interface with a fake
/// event source; no MSBuild needed. Coaching is asserted on the console
/// output the logger writes at diagnostic time: the inline design replaced
/// the old report.md + end-of-build pointer.
/// </summary>
[TestFixture]
public sealed class NudgeLoggerTests
{
    private string _tempDir = string.Empty;
    private string _originalCwd = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _originalCwd = Directory.GetCurrentDirectory();
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, ".nudge"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "rules"));
        // The logger resolves ./rules relative to the working directory.
        Directory.SetCurrentDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.SetCurrentDirectory(_originalCwd);
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static void WriteGuide(string dir, string ruleId) =>
        File.WriteAllText(Path.Combine(dir, ruleId + ".md"), $"""
            # {ruleId} — Test rule

            ## Why
            Test rationale.

            ## Do this
            1. Fix it.

            ## AVOID
            - Don't dodge it.
            """);

    private NudgeLogger NewLogger(string extraParameters = "") => new()
    {
        Parameters = $"baseline={Path.Combine(_tempDir, ".nudge", "baseline.txt")}{extraParameters}"
    };

    /// <summary>Runs the logger against the fake source, capturing Console.Out.</summary>
    private static string Run(NudgeLogger logger, Action<FakeEventSource> drive)
    {
        var events = new FakeEventSource();
        logger.Initialize(events);
        var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            drive(events);
        }
        finally
        {
            Console.SetOut(original);
        }
        return output.ToString();
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var i = 0;
        while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += needle.Length;
        }
        return count;
    }

    [Test]
    public void Diagnostic_PrintsFullCoachingInline()
    {
        WriteGuide(Path.Combine(_tempDir, "rules"), "T9999");
        var output = Run(NewLogger(), events =>
        {
            events.RaiseWarning("T9999", "Something smells.", Path.Combine(_tempDir, "src", "Foo.cs"), 12);
            events.RaiseBuildFinished(succeeded: true);
        });

        Assert.Multiple(() =>
        {
            Assert.That(output, Does.Contain("nudge: coaching for T9999 — Test rule"));
            Assert.That(output, Does.Contain("nudge: Why: Test rationale."));
            Assert.That(output, Does.Contain("nudge:   1. Fix it."));
            Assert.That(output, Does.Contain("nudge:   - Don't dodge it."));
            Assert.That(File.Exists(Path.Combine(_tempDir, ".nudge", "report.md")), Is.False,
                "the inline design writes no report file");
        });
    }

    [Test]
    public void Diagnostic_DedupesAndRemindsOnRepeatRule()
    {
        WriteGuide(Path.Combine(_tempDir, "rules"), "T9999");
        var output = Run(NewLogger(), events =>
        {
            var file = Path.Combine(_tempDir, "src", "Foo.cs");
            // Multi-targeted builds emit the same diagnostic once per framework.
            events.RaiseWarning("T9999", "Something smells.", file, 12);
            events.RaiseWarning("T9999", "Something smells.", file, 12);
            // A second location with the same rule gets the one-line reminder.
            events.RaiseWarning("T9999", "Something smells.", file, 40);
            events.RaiseBuildFinished(succeeded: true);
        });

        Assert.Multiple(() =>
        {
            Assert.That(CountOccurrences(output, "nudge: coaching for T9999"), Is.EqualTo(1),
                "the full guide prints once per rule per build");
            Assert.That(CountOccurrences(output, "nudge: T9999 — coaching printed"), Is.EqualTo(1),
                "a repeat of the rule gets exactly the reminder line");
        });
    }

    [Test]
    public void Diagnostic_ErrorEventsAreCoached()
    {
        WriteGuide(Path.Combine(_tempDir, "rules"), "T9999");
        var output = Run(NewLogger(), events =>
        {
            events.RaiseError("T9999", "Something broke.", Path.Combine(_tempDir, "src", "Foo.cs"), 3);
            events.RaiseBuildFinished(succeeded: false);
        });

        Assert.That(output, Does.Contain("nudge: coaching for T9999 — Test rule"));
    }

    [Test]
    public void Diagnostic_BaselinedFindingGetsNoCoaching()
    {
        WriteGuide(Path.Combine(_tempDir, "rules"), "T9999");
        var baselinePath = Path.Combine(_tempDir, ".nudge", "baseline.txt");
        // Baseline entries are RULEID|path-relative-to-repo-root.
        File.WriteAllText(baselinePath, "T9999|src/Foo.cs\n");
        var output = Run(NewLogger(), events =>
        {
            events.RaiseWarning("T9999", "Something smells.", Path.Combine(_tempDir, "src", "Foo.cs"), 12);
            events.RaiseBuildFinished(succeeded: true);
        });

        Assert.That(output, Does.Not.Contain("nudge:"),
            "a snoozed finding surfaces nothing at all");
    }

    [Test]
    public void Diagnostic_WithoutGuidePrintsNothing()
    {
        // No guide written for T0000: the generic fallback is a lookup
        // artifact (`nudge explain`), not something to spray into builds.
        var output = Run(NewLogger(), events =>
        {
            events.RaiseWarning("T0000", "Something smells.", Path.Combine(_tempDir, "src", "Foo.cs"), 12);
            events.RaiseBuildFinished(succeeded: true);
        });

        Assert.That(output, Does.Not.Contain("nudge:"));
    }

    [Test]
    public void Diagnostic_QuotedBaselineParameter_StillCoaches()
    {
        // `nudge init` quotes the baseline path in Directory.Build.rsp so repo
        // paths with spaces survive MSBuild's response-file parsing. A host
        // that passes the quotes through to the logger must still work.
        WriteGuide(Path.Combine(_tempDir, "rules"), "T9999");
        var baselinePath = Path.Combine(_tempDir, ".nudge", "baseline.txt");
        var logger = new NudgeLogger { Parameters = $"baseline=\"{baselinePath}\"" };
        var output = Run(logger, events =>
        {
            events.RaiseWarning("T9999", "Something smells.", Path.Combine(_tempDir, "src", "Foo.cs"), 12);
            events.RaiseBuildFinished(succeeded: true);
        });

        Assert.That(output, Does.Contain("nudge: coaching for T9999 — Test rule"));
    }

    [Test]
    public void ConsoleMode_PrintsDiagnosticsItselfWithCoachingAndSummary()
    {
        WriteGuide(Path.Combine(_tempDir, "rules"), "T9999");
        var file = Path.Combine(_tempDir, "src", "Foo.cs");
        var output = Run(NewLogger(";console=true"), events =>
        {
            events.RaiseError("T9999", "Something broke.", file, 3);
            events.RaiseWarning("T0000", "Unguided.", file, 9);
            events.RaiseBuildFinished(succeeded: false);
        });

        Assert.Multiple(() =>
        {
            // Canonical diagnostic lines, printed by the logger itself.
            Assert.That(output, Does.Contain($"{file}(3,0): error T9999: Something broke."));
            Assert.That(output, Does.Contain($"{file}(9,0): warning T0000: Unguided."));
            // Coaching accompanies the guided one.
            Assert.That(output, Does.Contain("nudge: coaching for T9999 — Test rule"));
            // Summary mirrors the default console logger's shape.
            Assert.That(output, Does.Contain("Build FAILED."));
            Assert.That(output, Does.Contain("1 Warning(s)"));
            Assert.That(output, Does.Contain("1 Error(s)"));
        });
    }

    [Test]
    public void DefaultMode_PrintsNoDiagnosticsOrSummary()
    {
        // Without console mode the default console logger owns diagnostic
        // and summary printing; nudge only adds coaching lines.
        WriteGuide(Path.Combine(_tempDir, "rules"), "T9999");
        var output = Run(NewLogger(), events =>
        {
            events.RaiseError("T9999", "Something broke.", Path.Combine(_tempDir, "src", "Foo.cs"), 3);
            events.RaiseBuildFinished(succeeded: false);
        });

        Assert.Multiple(() =>
        {
            Assert.That(output, Does.Not.Contain("error T9999:"));
            Assert.That(output, Does.Not.Contain("Build FAILED."));
        });
    }

    /// <summary>Minimal <see cref="IEventSource"/> stub: only the events the
    /// logger subscribes to are raisable; the rest are never fired.</summary>
    private sealed class FakeEventSource : IEventSource
    {
        // IEventSource requires all of these; the logger subscribes to
        // ErrorRaised, WarningRaised, MessageRaised and BuildFinished.
#pragma warning disable CS0067 // Event is never used (interface requirement)
        public event BuildMessageEventHandler? MessageRaised;
        public event BuildErrorEventHandler? ErrorRaised;
        public event BuildWarningEventHandler? WarningRaised;
        public event BuildStartedEventHandler? BuildStarted;
        public event BuildFinishedEventHandler? BuildFinished;
        public event ProjectStartedEventHandler? ProjectStarted;
        public event ProjectFinishedEventHandler? ProjectFinished;
        public event TargetStartedEventHandler? TargetStarted;
        public event TargetFinishedEventHandler? TargetFinished;
        public event TaskStartedEventHandler? TaskStarted;
        public event TaskFinishedEventHandler? TaskFinished;
        public event CustomBuildEventHandler? CustomEventRaised;
        public event BuildStatusEventHandler? StatusEventRaised;
        public event AnyEventHandler? AnyEventRaised;
#pragma warning restore CS0067

        public void RaiseWarning(string code, string message, string? file, int line) =>
            WarningRaised?.Invoke(this, new BuildWarningEventArgs(
                subcategory: null, code, file, line, 0, 0, 0, message,
                helpKeyword: null, senderName: "test"));

        public void RaiseError(string code, string message, string? file, int line) =>
            ErrorRaised?.Invoke(this, new BuildErrorEventArgs(
                subcategory: null, code, file, line, 0, 0, 0, message,
                helpKeyword: null, senderName: "test"));

        public void RaiseBuildFinished(bool succeeded = true) =>
            BuildFinished?.Invoke(this, new BuildFinishedEventArgs(
                "test", null, succeeded, DateTime.UtcNow));
    }
}
