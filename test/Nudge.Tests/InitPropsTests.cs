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
