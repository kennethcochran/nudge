using Nudge;
using NUnit.Framework;

namespace Nudge.Tests;

/// <summary>
/// Tests for the Directory.Build.props arrangement `nudge init` writes
/// (slice 2): the managed ErrorLog block's shape, the detection of a
/// user's own ErrorLog setting (detected and respected, never counted as
/// init's), and stripping that removes only init's block.
/// </summary>
[TestFixture]
public sealed class InitPropsTests
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

    private static readonly string[] UnmanagedGroup =
    [
        "  <PropertyGroup>",
        "    <ErrorLog>mine.sarif,version=2.1</ErrorLog>",
        "  </PropertyGroup>",
    ];

    private void WriteProps(params string[][] sections)
    {
        var lines = new List<string> { "<Project>" };
        foreach (var section in sections)
            lines.AddRange(section);
        lines.Add("</Project>");
        File.WriteAllLines(Path.Combine(_tempDir, "Directory.Build.props"), lines);
    }

    private string WriteSarifFile(string fileName)
    {
        var sarifDir = Path.Combine(_tempDir, ".nudge", "sarif");
        Directory.CreateDirectory(sarifDir);
        var path = Path.Combine(sarifDir, fileName);
        File.WriteAllText(path, "{}");
        return path;
    }

    private void RunRemoveInit()
    {
        // RemoveInit reports to the console; swallow it so the test log
        // stays readable.
        var original = Console.Out;
        Console.SetOut(new StringWriter());
        try
        {
            Program.RemoveInit(
                Path.Combine(_tempDir, ".nudge"),
                Path.Combine(_tempDir, "Directory.Build.rsp"),
                Path.Combine(_tempDir, "Directory.Build.props"));
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Test]
    public void RemoveInit_WithManagedBlock_RemovesSarifFiles()
    {
        WriteProps(Program.ErrorLogPropsBlockLines);
        var sarif = WriteSarifFile("Widget.sarif");

        RunRemoveInit();

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(_tempDir, "Directory.Build.props")), Is.False,
                "the props held nothing but the arrangement, so it is deleted outright");
            Assert.That(File.Exists(sarif), Is.False);
        });
    }

    [Test]
    public void RemoveInit_WithManagedBlockAndUnmanagedErrorLog_KeepsSarifFiles()
    {
        // The managed block is stripped, but an unmanaged ErrorLog may be
        // writing to the same directory — those files are the user's, not
        // init's, so removal must leave them alone.
        WriteProps(UnmanagedGroup, Program.ErrorLogPropsBlockLines);
        var sarif = WriteSarifFile("Widget.sarif");

        RunRemoveInit();

        var props = File.ReadAllText(Path.Combine(_tempDir, "Directory.Build.props"));
        Assert.Multiple(() =>
        {
            Assert.That(props, Does.Contain("<ErrorLog>mine.sarif,version=2.1</ErrorLog>"));
            Assert.That(props, Does.Not.Contain(Program.ErrorLogPropsLabel));
            Assert.That(File.Exists(sarif), Is.True);
        });
    }

    [Test]
    public void RemoveInit_WithoutManagedBlock_KeepsSarifFilesAndProps()
    {
        WriteProps(UnmanagedGroup);
        var sarif = WriteSarifFile("Widget.sarif");
        var before = File.ReadAllText(Path.Combine(_tempDir, "Directory.Build.props"));

        RunRemoveInit();

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(Path.Combine(_tempDir, "Directory.Build.props")),
                Is.EqualTo(before), "nothing of init's was there to remove");
            Assert.That(File.Exists(sarif), Is.True);
        });
    }

    [Test]
    public void ErrorLogPropsBlock_IsPerProjectSarifUnderNudgeDir()
    {
        var block = string.Join("\n", Program.ErrorLogPropsBlockLines);

        Assert.Multiple(() =>
        {
            Assert.That(block, Does.Contain($"Label=\"{Program.ErrorLogPropsLabel}\""));
            Assert.That(block, Does.Contain(
                "<ErrorLog>$(MSBuildThisFileDirectory).nudge/sarif/$(MSBuildProjectName).sarif,version=2.1</ErrorLog>"),
                "one SARIF per project, located from the props file's own directory");
        });
    }

    [Test]
    public void HasErrorLogPropsBlock_DetectsTheManagedGroup()
    {
        var lines = new List<string> { "<Project>" };
        lines.AddRange(Program.ErrorLogPropsBlockLines);
        lines.Add("</Project>");

        Assert.That(Program.HasErrorLogPropsBlock(lines), Is.True);
        Assert.That(Program.HasErrorLogPropsBlock(new[] { "<Project>", "</Project>" }), Is.False);
    }

    [Test]
    public void HasUnmanagedErrorLog_UsersOwnSettingIsDetected()
    {
        var lines = new[]
        {
            "<Project>",
            "  <PropertyGroup>",
            "    <ErrorLog>build.sarif,version=2.1</ErrorLog>",
            "  </PropertyGroup>",
            "</Project>",
        };

        Assert.That(Program.HasUnmanagedErrorLog(lines), Is.True);
    }

    [Test]
    public void HasUnmanagedErrorLog_ManagedBlockAloneIsNotUnmanaged()
    {
        var lines = new List<string> { "<Project>" };
        lines.AddRange(Program.ErrorLogPropsBlockLines);
        lines.Add("</Project>");

        Assert.That(Program.HasUnmanagedErrorLog(lines), Is.False);
    }

    [Test]
    public void HasUnmanagedErrorLog_UserSettingAlongsideManagedBlockIsStillDetected()
    {
        var lines = new List<string>
        {
            "<Project>",
            "  <PropertyGroup>",
            "    <ErrorLog>mine.sarif</ErrorLog>",
            "  </PropertyGroup>",
        };
        lines.AddRange(Program.ErrorLogPropsBlockLines);
        lines.Add("</Project>");

        Assert.That(Program.HasUnmanagedErrorLog(lines), Is.True);
    }

    [Test]
    public void StripErrorLogPropsLines_RemovesOnlyTheManagedBlock()
    {
        var userLines = new[]
        {
            "<Project>",
            "  <PropertyGroup>",
            "    <LangVersion>latest</LangVersion>",
            "  </PropertyGroup>",
            "</Project>",
        };
        var lines = new List<string>
        {
            "<Project>",
            "  <PropertyGroup>",
            "    <LangVersion>latest</LangVersion>",
            "  </PropertyGroup>",
        };
        lines.AddRange(Program.ErrorLogPropsBlockLines);
        lines.Add("</Project>");

        var remaining = Program.StripErrorLogPropsLines(lines);

        Assert.That(remaining, Is.EqualTo(userLines));
    }

    [Test]
    public void StripErrorLogPropsLines_NoBlock_PassesLinesThrough()
    {
        var lines = new[] { "<Project>", "  <PropertyGroup />", "</Project>" };

        Assert.That(Program.StripErrorLogPropsLines(lines), Is.EqualTo(lines));
    }

    [Test]
    public void IsOnlyEmptyProject_Cases()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Program.IsOnlyEmptyProject(
                new[] { "<Project>", "", "</Project>" }), Is.True);
            Assert.That(Program.IsOnlyEmptyProject(
                new[] { "<?xml version=\"1.0\" encoding=\"utf-8\"?>", "<Project>", "</Project>" }), Is.True);
            Assert.That(Program.IsOnlyEmptyProject(
                new[] { "<Project>", "  <PropertyGroup />", "</Project>" }), Is.False);
        });
    }
}
