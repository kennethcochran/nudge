using Nudge;
using NUnit.Framework;

namespace Nudge.Tests;

/// <summary>
/// Tests for <see cref="SkillInstaller"/> harness path resolution and install/uninstall.
/// </summary>
[TestFixture]
public sealed class SkillInstallerTests
{
    private string _home = string.Empty;
    private string _project = string.Empty;
    private string _source = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _home = TempDir();
        _project = TempDir();
        _source = TempDir();
        Directory.CreateDirectory(Path.Combine(_source, "references"));
        File.WriteAllText(Path.Combine(_source, "SKILL.md"), "# skill");
        File.WriteAllText(Path.Combine(_source, "references", "guide.md"), "# ref");
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var dir in new[] { _home, _project, _source })
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Test]
    public void ResolveTargets_DefaultHarnesses_GlobalScope_UsesUniversalPair()
    {
        var targets = SkillInstaller.ResolveTargets(
            SkillInstaller.DefaultHarnesses, globalScope: true, _home, _project);

        Assert.That(targets, Has.Count.EqualTo(2));
        Assert.That(targets, Contains.Item(
            Path.Combine(_home, ".agents", "skills", "nudge-ruleset-gen")));
        Assert.That(targets, Contains.Item(
            Path.Combine(_home, ".claude", "skills", "nudge-ruleset-gen")));
    }

    [Test]
    public void ResolveTargets_ProjectScope_UsesCwdRelativeDirs()
    {
        var targets = SkillInstaller.ResolveTargets(
            new[] { "cursor" }, globalScope: false, _home, _project);

        Assert.That(targets, Has.Count.EqualTo(1));
        Assert.That(targets[0], Is.EqualTo(
            Path.Combine(_project, ".cursor", "skills", "nudge-ruleset-gen")));
    }

    [Test]
    public void ResolveTargets_OverlappingHarnesses_Deduplicates()
    {
        // codex and agents share the .agents/skills directory.
        var targets = SkillInstaller.ResolveTargets(
            new[] { "codex", "agents" }, globalScope: true, _home, _project);

        Assert.That(targets, Has.Count.EqualTo(1));
    }

    [Test]
    public void ResolveTargets_UnknownHarness_ThrowsUsageError()
    {
        Assert.Throws<CliUsageException>(() =>
            SkillInstaller.ResolveTargets(new[] { "clippy" }, true, _home, _project));
    }

    [Test]
    public void Install_CopiesSkillFilesRecursively()
    {
        var target = Path.Combine(_home, ".agents", "skills", "nudge-ruleset-gen");
        var logs = new List<string>();

        var written = SkillInstaller.Install(
            _source, new[] { target }, force: false, dryRun: false, logs.Add);

        Assert.That(written, Has.Count.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(target, "SKILL.md")), Is.True);
        Assert.That(File.Exists(Path.Combine(target, "references", "guide.md")), Is.True);
    }

    [Test]
    public void Install_ExistingWithoutForce_Skips()
    {
        var target = Path.Combine(_home, ".agents", "skills", "nudge-ruleset-gen");
        var logs = new List<string>();
        SkillInstaller.Install(_source, new[] { target }, false, false, logs.Add);

        var written = SkillInstaller.Install(_source, new[] { target }, false, false, logs.Add);

        Assert.That(written, Is.Empty);
    }

    [Test]
    public void Install_DryRun_WritesNothing()
    {
        var target = Path.Combine(_home, ".agents", "skills", "nudge-ruleset-gen");
        var logs = new List<string>();

        SkillInstaller.Install(_source, new[] { target }, false, dryRun: true, logs.Add);

        Assert.That(Directory.Exists(target), Is.False);
    }

    [Test]
    public void Uninstall_RemovesSkillDirectory()
    {
        var target = Path.Combine(_home, ".agents", "skills", "nudge-ruleset-gen");
        var logs = new List<string>();
        SkillInstaller.Install(_source, new[] { target }, false, false, logs.Add);

        var removed = SkillInstaller.Uninstall(new[] { target }, dryRun: false, logs.Add);

        Assert.That(removed, Is.EqualTo(1));
        Assert.That(Directory.Exists(target), Is.False);
    }

    [Test]
    public void ListInstalled_FindsSkillAcrossHarnesses()
    {
        var logs = new List<string>();
        var targets = SkillInstaller.ResolveTargets(
            new[] { "cursor", "claude" }, globalScope: true, _home, _project);
        SkillInstaller.Install(_source, targets, false, false, logs.Add);

        var found = SkillInstaller.ListInstalled(_home, _project);

        Assert.That(found, Has.Count.EqualTo(2));
    }
}
