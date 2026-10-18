using System.Text.Json;
using Nudge;
using NUnit.Framework;

namespace Nudge.Tests;

/// <summary>
/// Tests for <see cref="SarifReader"/> — SARIF 2.1.0 runs/results mapped to
/// the engine's finding records, including the note/none levels no other
/// channel carries.
/// </summary>
[TestFixture]
public sealed class SarifReaderTests
{
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "errorlog.sarif");

    private static IReadOnlyList<BuildDiagnostic> ParseFixture() =>
        SarifReader.Parse(File.ReadAllText(FixturePath));

    // Acceptance (slice 1 of docs/plans/sarif-and-expansion.md): the fixture
    // is a real ErrorLog from a fixture build; its Info-severity (note)
    // findings must parse, alongside the warning and error findings.
    [Test]
    public void Parse_RealErrorLog_NoteWarningAndErrorFindingsAllSurface()
    {
        var diagnostics = ParseFixture();

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics, Has.Count.EqualTo(5));
            Assert.That(diagnostics.Count(d => d.RuleId == "CA1822"), Is.EqualTo(3),
                "the three Info-severity CA1822 findings");
            Assert.That(diagnostics.Where(d => d.RuleId == "CA1822")
                .All(d => d.Severity == "note"), Is.True,
                "Info severity arrives as SARIF level 'note' and must be preserved, not promoted to warning");
            Assert.That(diagnostics.Single(d => d.RuleId == "CA1825").IsError, Is.True);
            Assert.That(diagnostics.Single(d => d.RuleId == "CS0219").Severity, Is.EqualTo("warning"));
        });
    }

    [Test]
    public void Parse_RealErrorLog_FileUriIsDecodedAndLocationKept()
    {
        var announce = ParseFixture().Single(
            d => d.RuleId == "CA1822" && d.Message.Contains("Announce"));

        Assert.Multiple(() =>
        {
            // The real URI is file:///tmp/nudge%20sarif%20fixture/... — the
            // percent-escapes must decode to the real path, spaces included.
            Assert.That(announce.File, Is.EqualTo("/tmp/nudge sarif fixture/Fixture/Widget.cs"));
            Assert.That(announce.Line, Is.EqualTo(6));
            Assert.That(announce.Column, Is.EqualTo(17));
            Assert.That(announce.Message, Does.Contain("does not access instance data"));
        });
    }

    [Test]
    public void Parse_ResultWithoutLevel_UsesRuleDefaultConfiguration()
    {
        const string sarif = """
            {
              "version": "2.1.0",
              "runs": [{
                "tool": { "driver": { "name": "test", "rules": [
                  { "id": "X1000", "defaultConfiguration": { "level": "note" } }
                ] } },
                "results": [{
                  "ruleId": "X1000",
                  "message": { "text": "info-level finding" },
                  "locations": [{ "physicalLocation": {
                    "artifactLocation": { "uri": "A.cs" },
                    "region": { "startLine": 3, "startColumn": 5 } } }]
                }]
              }]
            }
            """;

        var diagnostics = SarifReader.Parse(sarif);

        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].Severity, Is.EqualTo("note"));
    }

    [Test]
    public void Parse_ResultWithoutLevelOrRuleDefault_DefaultsToWarning()
    {
        const string sarif = """
            {
              "version": "2.1.0",
              "runs": [{
                "tool": { "driver": { "name": "test", "rules": [] } },
                "results": [{
                  "ruleId": "X1000",
                  "message": { "text": "plain finding" }
                }]
              }]
            }
            """;

        var diagnostics = SarifReader.Parse(sarif);

        Assert.That(diagnostics[0].Severity, Is.EqualTo("warning"));
    }

    [Test]
    public void Parse_NoneLevel_IsPreserved()
    {
        const string sarif = """
            {
              "version": "2.1.0",
              "runs": [{
                "tool": { "driver": { "name": "test", "rules": [] } },
                "results": [{
                  "ruleId": "X1000", "level": "none",
                  "message": { "text": "not a problem" }
                }]
              }]
            }
            """;

        var diagnostics = SarifReader.Parse(sarif);

        Assert.That(diagnostics[0].Severity, Is.EqualTo("none"));
    }

    [Test]
    public void Parse_ResultWithoutRuleId_ResolvesViaRuleIndex()
    {
        const string sarif = """
            {
              "version": "2.1.0",
              "runs": [{
                "tool": { "driver": { "name": "test", "rules": [
                  { "id": "AA0001" }, { "id": "BB0002" }
                ] } },
                "results": [{
                  "ruleIndex": 1, "level": "warning",
                  "message": { "text": "indexed finding" }
                }]
              }]
            }
            """;

        var diagnostics = SarifReader.Parse(sarif);

        Assert.That(diagnostics[0].RuleId, Is.EqualTo("BB0002"));
    }

    [Test]
    public void Parse_MessageId_ResolvesAgainstRuleMessageStringsWithArguments()
    {
        const string sarif = """
            {
              "version": "2.1.0",
              "runs": [{
                "tool": { "driver": { "name": "test", "rules": [
                  { "id": "X1000", "messageStrings": {
                      "greeting": { "text": "Hello {0}" } } }
                ] } },
                "results": [{
                  "ruleId": "X1000", "level": "note",
                  "message": { "id": "greeting", "arguments": ["world"] }
                }]
              }]
            }
            """;

        var diagnostics = SarifReader.Parse(sarif);

        Assert.That(diagnostics[0].Message, Is.EqualTo("Hello world"));
    }

    [Test]
    public void Parse_RelativeUri_ResolvesAgainstBaseDirectory()
    {
        const string sarif = """
            {
              "version": "2.1.0",
              "runs": [{
                "tool": { "driver": { "name": "test", "rules": [] } },
                "results": [{
                  "ruleId": "X1000", "level": "warning",
                  "message": { "text": "relative" },
                  "locations": [{ "physicalLocation": {
                    "artifactLocation": { "uri": "src/A.cs" },
                    "region": { "startLine": 1 } } }]
                }]
              }]
            }
            """;

        var diagnostics = SarifReader.Parse(sarif, baseDirectory: "/repo root");

        Assert.That(diagnostics[0].File,
            Is.EqualTo(Path.GetFullPath(Path.Combine("/repo root", "src/A.cs"))));
    }

    [Test]
    public void Parse_DuplicateResults_AreCollapsed()
    {
        const string result = """
            { "ruleId": "X1000", "level": "warning",
              "message": { "text": "same finding" },
              "locations": [{ "physicalLocation": {
                "artifactLocation": { "uri": "A.cs" },
                "region": { "startLine": 2 } } }] }
            """;
        var sarif = $$"""
            {
              "version": "2.1.0",
              "runs": [{
                "tool": { "driver": { "name": "test", "rules": [] } },
                "results": [{{result}}, {{result}}]
              }]
            }
            """;

        var diagnostics = SarifReader.Parse(sarif);

        Assert.That(diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public void Parse_InvalidJson_ThrowsLoudly()
    {
        // JsonException or a subclass (JsonReaderException on current runtimes).
        Assert.That(() => SarifReader.Parse("{ not json"),
            Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void Parse_JsonWithoutRuns_ThrowsFormatException()
    {
        Assert.That(() => SarifReader.Parse("""{ "version": "2.1.0" }"""),
            Throws.TypeOf<FormatException>());
    }

    [Test]
    public void Parse_UnsupportedVersion_ThrowsFormatException()
    {
        Assert.That(() => SarifReader.Parse("""{ "version": "1.0.0", "runs": [] }"""),
            Throws.TypeOf<FormatException>());
    }

    // The slice-1 acceptance pipeline end to end at engine level: the real
    // ErrorLog's Info-severity finding is coached (with a real guide)
    // alongside the warning/error findings, and nothing is dropped for
    // lacking a guide — uncatalogued rules surface as uncoached findings.
    [Test]
    public void CoachPipeline_RealErrorLog_InfoFindingIsCoachedAlongsideWarningAndError()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "CA1822.md"), """
                # CA1822 — Mark members as static

                ## Why
                A member that never touches instance state misleads readers about what it depends on.

                ## Do this
                1. Mark the member static, or use instance state if it truly belongs to the instance.

                ## AVOID
                - Do not suppress CA1822 to keep the call sites pretty.
                """);
            var catalog = new RuleCatalog(tempDir);
            var diagnostics = ParseFixture();

            var result = CoachingEngine.Generate(
                diagnostics, catalog, new HashSet<string>(),
                "/tmp/nudge sarif fixture/Fixture", "errorlog.sarif", buildFailed: false);

            Assert.Multiple(() =>
            {
                Assert.That(result.FindingCount, Is.EqualTo(5),
                    "every SARIF result surfaces — uncatalogued rules are uncoached findings, never dropped");
                Assert.That(result.RuleCount, Is.EqualTo(3));
                Assert.That(result.HasErrors, Is.True, "the CA1825 error is in the log");
                Assert.That(result.Report, Does.Contain("misleads readers about what it depends on"),
                    "the Info-severity CA1822 finding's coaching appears in the report");
                Assert.That(result.Report, Does.Contain("CA1822 · "), "CA1822 group header");
                Assert.That(result.Report, Does.Contain("(3 note issue(s))"),
                    "a pure note group is labelled note, not warning");
                Assert.That(result.Report, Does.Contain("CA1825"), "error finding coached alongside");
                Assert.That(result.Report, Does.Contain("CS0219"), "warning finding coached alongside");
                Assert.That(result.Report, Does.Contain("Widget.cs:6"),
                    "paths shorten against the root dir");
                Assert.That(result.FallbackRuleIds, Is.EquivalentTo(new[] { "CA1825", "CS0219" }),
                    "rules without guides report as fallback (uncoached), CA1822 does not");
            });
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public void CoachPipeline_BaselinedSarifFinding_IsSnoozed()
    {
        var diagnostics = ParseFixture();
        var baseline = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CA1822|Widget.cs",
        };

        var result = CoachingEngine.Generate(
            diagnostics, new RuleCatalog((string?)null), baseline,
            "/tmp/nudge sarif fixture/Fixture", "errorlog.sarif", buildFailed: false);

        Assert.Multiple(() =>
        {
            // Baseline semantics are source-independent: the same
            // RULEID|path entry that snoozes a text-log finding snoozes
            // the SARIF-delivered one.
            Assert.That(result.FindingCount, Is.EqualTo(2));
            Assert.That(result.Report, Does.Not.Contain("CA1822"));
        });
    }
}
