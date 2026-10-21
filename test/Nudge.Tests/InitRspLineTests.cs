using Nudge;
using NUnit.Framework;

namespace Nudge.Tests;

/// <summary>
/// Tests for the Directory.Build.rsp line `nudge init` writes: the logger and
/// baseline paths must be quoted (MSBuild expands %MSBuildThisFileDirectory%
/// before splitting response-file lines on unquoted whitespace, so an
/// unquoted line fails every build in a repo whose path contains spaces),
/// and init must only ever strip lines it owns.
/// </summary>
[TestFixture]
public sealed class InitRspLineTests
{
    [Test]
    public void InitLoggerLine_QuotesLoggerAndBaselinePaths()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Program.InitLoggerLine,
                Does.Contain("\"%MSBuildThisFileDirectory%.nudge/Nudge.Logger.dll\""));
            Assert.That(Program.InitLoggerLine,
                Does.Contain("baseline=\"%MSBuildThisFileDirectory%.nudge/baseline.txt\""));
        });
    }

    [Test]
    public void InitLoggerLine_QuotesSarifDirPath()
    {
        Assert.That(Program.InitLoggerLine,
            Does.Contain("sarifdir=\"%MSBuildThisFileDirectory%.nudge/sarif\""),
            "the logger's SARIF-finish directory travels quoted, like the other paths");
    }

    [Test]
    public void IsInitManagedLine_MatchesMarkerAndOwnRegistration()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Program.IsInitManagedLine(Program.InitMarker + " — loads the nudge MSBuild logger"), Is.True);
            Assert.That(Program.IsInitManagedLine(Program.InitLoggerLine), Is.True);
        });
    }

    [Test]
    public void IsInitManagedLine_LeavesUserLinesAlone()
    {
        Assert.Multiple(() =>
        {
            // A comment that merely mentions the logger is the user's text.
            Assert.That(Program.IsInitManagedLine("# We tried Nudge.Logger once; not again."), Is.False);
            // Another logger's registration is not init's to strip.
            Assert.That(Program.IsInitManagedLine("/logger:Other.OtherLogger,tools/Other.dll"), Is.False);
            // Unrelated switches pass through untouched.
            Assert.That(Program.IsInitManagedLine("/warnaserror"), Is.False);
        });
    }

    [Test]
    public void ConsoleLoggerLine_IsInitManagedAndCarriesTheParameter()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Program.InitConsoleLoggerLine, Does.Contain(";console=true"));
            Assert.That(Program.IsInitManagedLine(Program.InitConsoleLoggerLine), Is.True);
        });
    }

    [Test]
    public void StripInitLines_RemovesConsoleModeSwitchFromAnInitInstall()
    {
        var lines = new[]
        {
            "/warnaserror",
            Program.InitMarker + " — loads the nudge MSBuild logger",
            Program.InitNoConsoleLine,
            Program.InitConsoleLoggerLine,
        };

        var remaining = Program.StripInitLines(lines);

        Assert.That(remaining, Is.EqualTo(new[] { "/warnaserror" }));
    }

    [Test]
    public void StripInitLines_KeepsAUsersOwnNoConsoleLoggerSwitch()
    {
        // No init installation in the file: the generic switch is the user's.
        var lines = new[] { Program.InitNoConsoleLine, "/warnaserror" };

        var remaining = Program.StripInitLines(lines);

        Assert.That(remaining, Is.EqualTo(lines));
    }
}
