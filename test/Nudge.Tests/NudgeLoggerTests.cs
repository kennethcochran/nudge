using Microsoft.Build.Framework;
using Nudge.Logger;
using NUnit.Framework;

namespace Nudge.Tests;

/// <summary>
/// Tests for <see cref="NudgeLogger"/> — the MSBuild logger. Drives the
/// logger through the public <see cref="ILogger"/> interface with a fake
/// event source; no MSBuild needed.
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

    [Test]
    public void Finish_WritesCoachingReportNextToBaseline()
    {
        WriteGuide(Path.Combine(_tempDir, "rules"), "T9999");
        var logger = new NudgeLogger
        {
            Parameters = $"baseline={Path.Combine(_tempDir, ".nudge", "baseline.txt")}"
        };
        var events = new FakeEventSource();
        logger.Initialize(events);

        events.RaiseWarning("T9999", "Something smells.", Path.Combine(_tempDir, "src", "Foo.cs"), 12);
        events.RaiseBuildFinished();

        var reportPath = Path.Combine(_tempDir, ".nudge", "report.md");
        Assert.That(File.Exists(reportPath), Is.True, "report.md should be written next to the baseline");
        var report = File.ReadAllText(reportPath);
        Assert.Multiple(() =>
        {
            Assert.That(report, Does.Contain("T9999 — Test rule"));
            Assert.That(report, Does.Contain("Test rationale."));
            Assert.That(report, Does.Contain("**Do this:**"));
            Assert.That(report, Does.Contain("**AVOID:**"));
        });
    }

    [Test]
    public void Finish_DedupesRepeatedDiagnostics()
    {
        WriteGuide(Path.Combine(_tempDir, "rules"), "T9999");
        var logger = new NudgeLogger
        {
            Parameters = $"baseline={Path.Combine(_tempDir, ".nudge", "baseline.txt")}"
        };
        var events = new FakeEventSource();
        logger.Initialize(events);

        // Multi-targeted builds emit the same diagnostic once per framework.
        var file = Path.Combine(_tempDir, "src", "Foo.cs");
        events.RaiseWarning("T9999", "Something smells.", file, 12);
        events.RaiseWarning("T9999", "Something smells.", file, 12);
        events.RaiseBuildFinished();

        var report = File.ReadAllText(Path.Combine(_tempDir, ".nudge", "report.md"));
        Assert.That(report, Does.Contain("1 warning issue(s)"));
    }

    [Test]
    public void Finish_RespectsBaseline()
    {
        WriteGuide(Path.Combine(_tempDir, "rules"), "T9999");
        var baselinePath = Path.Combine(_tempDir, ".nudge", "baseline.txt");
        // Baseline entries are RULEID|path-relative-to-repo-root.
        File.WriteAllText(baselinePath, "T9999|src/Foo.cs\n");
        var logger = new NudgeLogger { Parameters = $"baseline={baselinePath}" };
        var events = new FakeEventSource();
        logger.Initialize(events);

        events.RaiseWarning("T9999", "Something smells.", Path.Combine(_tempDir, "src", "Foo.cs"), 12);
        events.RaiseBuildFinished();

        var report = File.ReadAllText(Path.Combine(_tempDir, ".nudge", "report.md"));
        Assert.That(report, Does.Contain("Clean — no findings."));
    }

    [Test]
    public void Finish_PrintsOneLinePointer()
    {
        WriteGuide(Path.Combine(_tempDir, "rules"), "T9999");
        var logger = new NudgeLogger
        {
            Parameters = $"baseline={Path.Combine(_tempDir, ".nudge", "baseline.txt")}"
        };
        var events = new FakeEventSource();
        logger.Initialize(events);

        var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            events.RaiseWarning("T9999", "Something smells.", Path.Combine(_tempDir, "src", "Foo.cs"), 12);
            events.RaiseBuildFinished();
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.That(output.ToString(), Does.Contain("nudge: 1 finding(s) across 1 rule(s)"));
    }

    /// <summary>Minimal <see cref="IEventSource"/> stub: only the events the
    /// logger subscribes to are raisable; the rest are never fired.</summary>
    private sealed class FakeEventSource : IEventSource
    {
        // IEventSource requires all of these; the logger only subscribes to
        // ErrorRaised, WarningRaised and BuildFinished.
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

        public void RaiseBuildFinished() =>
            BuildFinished?.Invoke(this, new BuildFinishedEventArgs(
                "test", null, succeeded: true, DateTime.UtcNow));
    }
}
