using NUnit.Framework;

namespace Nudge.Tests;

/// <summary>
/// Tests for the skills shipped in the repo (<c>.agents/skills/</c>): every
/// skill the installer knows about must exist there, its folder name must
/// match its frontmatter <c>name</c> (harnesses require this), and the
/// frontmatter must carry a discovery <c>description</c>. The coaching skill
/// additionally pins the loop it teaches: report → explain → fix → rebuild.
/// </summary>
[TestFixture]
public sealed class ShippedSkillTests
{
    private static string FindSkillDir(string skillName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, ".agents", "skills", skillName);
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        Assert.Fail($"Could not locate .agents/skills/{skillName} from test run directory.");
        throw new InvalidOperationException("unreachable");
    }

    [TestCase("nudge-ruleset-gen")]
    [TestCase("nudge-coaching")]
    public void ShippedSkill_FrontmatterNameMatchesFolderAndHasDescription(string skillName)
    {
        var skillFile = Path.Combine(FindSkillDir(skillName), "SKILL.md");
        Assert.That(File.Exists(skillFile), Is.True, $"{skillName}: SKILL.md missing");

        var text = File.ReadAllText(skillFile);
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.StartWith("---"), $"{skillName}: SKILL.md must open with YAML frontmatter");
            Assert.That(text, Does.Contain($"name: {skillName}"),
                $"{skillName}: frontmatter name must match the folder name");
            Assert.That(text, Does.Contain("description:"),
                $"{skillName}: frontmatter must carry a discovery description");
        });
    }

    [Test]
    public void CoachingSkill_TeachesTheReportLoop()
    {
        var text = File.ReadAllText(
            Path.Combine(FindSkillDir("nudge-coaching"), "SKILL.md"));

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain(".nudge/report.md"),
                "coaching skill must point agents at the build report");
            Assert.That(text, Does.Contain("nudge explain"),
                "coaching skill must cover the on-demand guide lookup");
            Assert.That(text, Does.Contain("nudge init"),
                "coaching skill must cover the not-installed case");
            Assert.That(text, Does.Contain("baseline"),
                "coaching skill must address the baseline");
        });
    }
}
