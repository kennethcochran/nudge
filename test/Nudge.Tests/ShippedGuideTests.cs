using Nudge;
using NUnit.Framework;

namespace Nudge.Tests;

/// <summary>
/// Tests for the Sonar ruleset's coaching guides
/// (<c>rulesets/Nudge.Rules.Sonar/rules/</c>): the generated SonarSource rule
/// guides validated by the three-way experiment (see <c>src/Nudge/EXPERIMENT.md</c>).
/// </summary>
[TestFixture]
public sealed class ShippedGuideTests
{
    private static string FindRulesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "rulesets", "Nudge.Rules.Sonar", "rules");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        Assert.Fail("Could not locate rulesets/Nudge.Rules.Sonar/rules from test run directory.");
        throw new InvalidOperationException("unreachable");
    }

    private static RuleCatalog Catalog() => new(FindRulesDir());

    [Test]
    public void Resolve_DeprecatedRuleGuides_MentionDeprecation()
    {
        var catalog = Catalog();

        foreach (var ruleId in new[] { "S1227", "S2387", "S3884", "S4212", "S4214", "S4792", "S6803" })
        {
            var guide = catalog.Resolve(ruleId, "raw message");

            Assert.Multiple(() =>
            {
                Assert.That(guide.Why, Does.Contain("deprecated").IgnoreCase,
                    $"{ruleId}: guide should state the rule is deprecated");
                Assert.That(guide.DoThis, Is.Not.Empty, $"{ruleId}: guide should have remediation steps");
                Assert.That(guide.Avoid, Is.Not.Empty, $"{ruleId}: guide should have anti-patterns");
            });
        }
    }

    [Test]
    public void Resolve_S2696_WarnsAgainstInterlockedAsFix()
    {
        // Regression test: an earlier version of this guide listed "guard it
        // (lock, Interlocked, ...)" as an acceptable fix, which the three-way
        // experiment showed legitimizes a non-fix (S2696|generated was gamed).
        var guide = Catalog().Resolve("S2696", "raw message");

        var avoidText = string.Join("\n", guide.Avoid);
        Assert.That(avoidText, Does.Contain("Interlocked").IgnoreCase,
            "S2696 guide must explicitly warn that wrapping the write in Interlocked is not a fix");
    }

    [Test]
    public void Resolve_AllShippedGuides_ParseToNonEmptySections()
    {
        var catalog = Catalog();
        var failures = new List<string>();

        foreach (var file in Directory.GetFiles(FindRulesDir(), "*.md"))
        {
            var ruleId = Path.GetFileNameWithoutExtension(file);
            var guide = catalog.Resolve(ruleId, "raw message");

            if (string.IsNullOrWhiteSpace(guide.Why) || guide.Why.Contains("No rationale recorded"))
                failures.Add($"{ruleId}: empty Why");
            if (guide.DoThis.Count == 0)
                failures.Add($"{ruleId}: empty Do-this");
            if (guide.Avoid.Count == 0)
                failures.Add($"{ruleId}: empty AVOID");
        }

        Assert.That(failures, Is.Empty,
            $"Guides with structural problems:\n{string.Join("\n", failures)}");
    }

    [Test]
    public void ShippedGuideCount_CoversKnownCorpus()
    {
        // The generated SonarSource guides cover the analyzer corpus; this
        // guards against accidental deletion of the ruleset content.
        var count = Directory.GetFiles(FindRulesDir(), "S*.md").Length;

        Assert.That(count, Is.GreaterThanOrEqualTo(500),
            $"Expected 500+ shipped S-rule guides, found {count}");
    }

    [Test]
    public void RulesetManifest_IsValid()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? manifestPath = null;
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "rulesets", "Nudge.Rules.Sonar", "ruleset.json");
            if (File.Exists(candidate))
            {
                manifestPath = candidate;
                break;
            }
            dir = dir.Parent;
        }
        Assert.That(manifestPath, Is.Not.Null, "ruleset.json not found");

        var manifest = RulesetManifest.Load(manifestPath!);
        Assert.Multiple(() =>
        {
            Assert.That(manifest.Name, Is.EqualTo("sonar"));
            Assert.That(manifest.PackageId, Is.EqualTo("Nudge.Rules.Sonar"));
            Assert.That(manifest.RulePrefixes, Does.Contain("S"));
            Assert.That(manifest.AnalyzerPackages, Does.Contain("SonarAnalyzer.CSharp"));
        });
    }
}

/// <summary>
/// Tests for <see cref="RulesetManager"/> install/list/remove against a
/// temporary HOME-style rulesets directory.
/// </summary>
[TestFixture]
public sealed class RulesetManagerTests
{
    [Test]
    public void AnalyzerDetector_DetectsKnownPackages_FromCsproj()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "test.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="SonarAnalyzer.CSharp" Version="10.0.0" />
                    <PackageReference Include="Newtonsoft.Json" Version="13.0.0" />
                  </ItemGroup>
                </Project>
                """);

            var detected = AnalyzerDetector.DetectAnalyzers(tempDir);

            Assert.That(detected, Does.Contain("SonarAnalyzer.CSharp"));
            Assert.That(detected, Does.Not.Contain("Newtonsoft.Json"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public void AnalyzerDetector_MapsPackageToRuleset()
    {
        Assert.That(AnalyzerDetector.AnalyzerToRuleset["SonarAnalyzer.CSharp"], Is.EqualTo("sonar"));
        Assert.That(AnalyzerDetector.AnalyzerToRuleset["StyleCop.Analyzers"], Is.EqualTo("stylecop"));
    }
}
