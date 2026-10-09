using Nudge;
using NUnit.Framework;

namespace Nudge.Tests;

/// <summary>
/// Tests for <see cref="RuleCatalog"/> resolution order:
/// hand-written guide &gt; harvested analyzer metadata &gt; generic fallback.
/// </summary>
[TestFixture]
public sealed class RuleCatalogTests
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

    [Test]
    public void Resolve_HandWrittenGuideExists_PrefersItOverHarvestedMetadata()
    {
        File.WriteAllText(Path.Combine(_tempDir, "CUSTOM001.md"), """
            # CUSTOM001 — Custom Title From Guide

            ## Why
            Custom rationale.

            ## Do this
            1. Custom step.

            ## AVOID
            - Custom anti-pattern.
            """);
        var catalog = new RuleCatalog(_tempDir);

        var guide = catalog.Resolve("CUSTOM001", "raw message");

        Assert.Multiple(() =>
        {
            Assert.That(guide.Title, Is.EqualTo("CUSTOM001 — Custom Title From Guide"));
            Assert.That(guide.Why, Is.EqualTo("Custom rationale."));
            Assert.That(guide.DoThis, Is.EqualTo(new[] { "Custom step." }));
            Assert.That(guide.Avoid, Is.EqualTo(new[] { "Custom anti-pattern." }));
        });
    }

    [Test]
    public void Resolve_RuleIdLookup_IsCaseInsensitive()
    {
        File.WriteAllText(Path.Combine(_tempDir, "S1234.md"), """
            # S1234 — Some Rule

            ## Why
            Rationale.

            ## Do this
            1. Step.

            ## AVOID
            - Anti-pattern.
            """);
        var catalog = new RuleCatalog(_tempDir);

        var guide = catalog.Resolve("s1234", "raw message");

        Assert.That(guide.Title, Is.EqualTo("S1234 — Some Rule"));
    }

    [Test]
    public void Resolve_UnknownRuleId_FallsBackToGenericGuide()
    {
        var catalog = new RuleCatalog(_tempDir);

        var guide = catalog.Resolve("S9999", "Something is off.");

        Assert.Multiple(() =>
        {
            Assert.That(guide.RuleId, Is.EqualTo("S9999"));
            Assert.That(guide.Why, Does.Contain("Something is off."));
            Assert.That(guide.Avoid, Has.Some.Contains("cosmetic"));
        });
    }

    [Test]
    public void Resolve_GuideWithWrappedListItems_JoinsContinuationLines()
    {
        File.WriteAllText(Path.Combine(_tempDir, "S1234.md"), """
            # S1234 — Wrapped

            ## Why
            Rationale.

            ## Do this
            1. First step that wraps
               onto a second line.
            2. Second step.

            ## AVOID
            - Anti-pattern that wraps
              onto a second line.
            """);
        var catalog = new RuleCatalog(_tempDir);

        var guide = catalog.Resolve("S1234", "raw");

        Assert.Multiple(() =>
        {
            Assert.That(guide.DoThis[0], Is.EqualTo("First step that wraps onto a second line."));
            Assert.That(guide.DoThis[1], Is.EqualTo("Second step."));
            Assert.That(guide.Avoid[0], Is.EqualTo("Anti-pattern that wraps onto a second line."));
        });
    }
}
