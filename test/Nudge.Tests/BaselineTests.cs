using Nudge;
using NUnit.Framework;

namespace Nudge.Tests;

/// <summary>
/// Tests for <see cref="Baseline"/> snooze-file load, match, and write.
/// </summary>
[TestFixture]
public sealed class BaselineTests
{
    private string _tempDir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static BuildDiagnostic Diag(string ruleId, string? file, int line = 10) =>
        new(file, line, 1, "warning", ruleId, "Some message.", null);

    [Test]
    public void IsSnoozed_MatchingRuleAndFile_ReturnsTrueRegardlessOfLine()
    {
        var baselinePath = Path.Combine(_tempDir, "baseline.txt");
        File.WriteAllText(baselinePath, "S1234|src/Foo/Bar.cs\n");
        var entries = Baseline.Load(baselinePath);

        var snoozed = Baseline.IsSnoozed(Diag("S1234", "/repo/src/Foo/Bar.cs", line: 99), entries, "/repo");

        Assert.That(snoozed, Is.True);
    }

    [Test]
    public void IsSnoozed_DifferentRule_ReturnsFalse()
    {
        var baselinePath = Path.Combine(_tempDir, "baseline.txt");
        File.WriteAllText(baselinePath, "S1234|src/Foo/Bar.cs\n");
        var entries = Baseline.Load(baselinePath);

        var snoozed = Baseline.IsSnoozed(Diag("S5678", "/repo/src/Foo/Bar.cs"), entries, "/repo");

        Assert.That(snoozed, Is.False);
    }

    [Test]
    public void Load_MissingFile_ReturnsEmptyBaseline()
    {
        var entries = Baseline.Load(Path.Combine(_tempDir, "does-not-exist.txt"));

        Assert.That(entries, Is.Empty);
    }

    [Test]
    public void Write_ThenLoad_RoundTripsFindings()
    {
        var baselinePath = Path.Combine(_tempDir, "baseline.txt");
        var diagnostics = new[]
        {
            Diag("S1234", "/repo/src/Foo/Bar.cs"),
            Diag("GVM003", "/repo/src/Baz/Qux.cs"),
        };

        Baseline.Write(baselinePath, diagnostics, "/repo");
        var entries = Baseline.Load(baselinePath);

        Assert.Multiple(() =>
        {
            Assert.That(Baseline.IsSnoozed(diagnostics[0], entries, "/repo"), Is.True);
            Assert.That(Baseline.IsSnoozed(diagnostics[1], entries, "/repo"), Is.True);
            Assert.That(Baseline.IsSnoozed(Diag("S9999", "/repo/src/Other.cs"), entries, "/repo"), Is.False);
        });
    }
}
