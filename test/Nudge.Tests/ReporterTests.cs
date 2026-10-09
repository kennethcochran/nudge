using Nudge;
using NUnit.Framework;

namespace Nudge.Tests;

/// <summary>
/// Tests for <see cref="Reporter"/> — the coaching-report renderer.
/// </summary>
[TestFixture]
public sealed class ReporterTests
{
    [Test]
    public void Render_RationaleAppearsBeforeFileList()
    {
        // The GameVM-era GVM003 hand-written guide no longer ships with the
        // standalone tool, so this test brings its own guide via a temp dir.
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "GVM003.md"), """
                # GVM003 — No LINQ in optimizer passes

                ## Why
                LINQ introduces hidden allocations that hurt the tight loop.

                ## Do this
                1. Rewrite the loop without LINQ.

                ## AVOID
                - Do not just move the LINQ call into a helper.
                """);
            var diagnostics = new[]
            {
                new BuildDiagnostic("/repo/src/Foo/Bar.cs", 12, 1, "warning", "GVM003", "LINQ is prohibited here.", null),
            };
            var groups = diagnostics.GroupBy(d => d.RuleId, StringComparer.OrdinalIgnoreCase).ToList();
            var catalog = new RuleCatalog(tempDir);

            var report = Reporter.Render(groups, catalog, "/repo", "test.sln", buildFailed: false);

            var whyIndex = report.IndexOf("hidden allocations", StringComparison.Ordinal);
            var fileIndex = report.IndexOf("src/Foo/Bar.cs:12", StringComparison.Ordinal);
            var avoidIndex = report.IndexOf("**AVOID:**", StringComparison.Ordinal);

            Assert.Multiple(() =>
            {
                Assert.That(whyIndex, Is.GreaterThanOrEqualTo(0), "Why section missing");
                Assert.That(avoidIndex, Is.GreaterThan(whyIndex), "AVOID section should follow the rationale");
                Assert.That(fileIndex, Is.GreaterThan(avoidIndex), "File list should come after the coaching guide");
                Assert.That(report, Does.Contain("GVM003"));
                Assert.That(report, Does.Contain("Do this:"));
            });
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public void Render_BuildFailed_IncludesFailureNotice()
    {
        var diagnostics = new[]
        {
            new BuildDiagnostic("/repo/src/Foo/Bar.cs", 1, 1, "error", "CS1002", "; expected", null),
        };
        var groups = diagnostics.GroupBy(d => d.RuleId, StringComparer.OrdinalIgnoreCase).ToList();

        var report = Reporter.Render(groups, new RuleCatalog((string?)null), "/repo", "test.sln", buildFailed: true);

        Assert.That(report, Does.Contain("build FAILED"));
    }

    [Test]
    public void RenderClean_ReportsNoFindings()
    {
        var report = Reporter.RenderClean("test.sln");

        Assert.That(report, Does.Contain("Clean — no findings."));
    }
}
