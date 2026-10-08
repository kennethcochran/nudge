using Nudge;
using NUnit.Framework;

namespace Nudge.Tests;

/// <summary>
/// Tests for <see cref="LogParser"/> — the MSBuild diagnostic line parser.
/// </summary>
[TestFixture]
public sealed class LogParserTests
{
    [Test]
    public void Parse_LocatedWarningLine_ReturnsDiagnosticWithAllFields()
    {
        const string log = "/repo/src/Foo/Bar.cs(12,34): warning S1234: Cognitive complexity is too high. [/repo/src/Foo/Foo.csproj]";

        var result = LogParser.Parse(log);

        Assert.That(result, Has.Count.EqualTo(1));
        var d = result[0];
        Assert.Multiple(() =>
        {
            Assert.That(d.File, Is.EqualTo("/repo/src/Foo/Bar.cs"));
            Assert.That(d.Line, Is.EqualTo(12));
            Assert.That(d.Column, Is.EqualTo(34));
            Assert.That(d.Severity, Is.EqualTo("warning"));
            Assert.That(d.RuleId, Is.EqualTo("S1234"));
            Assert.That(d.Message, Is.EqualTo("Cognitive complexity is too high."));
            Assert.That(d.Project, Is.EqualTo("/repo/src/Foo/Foo.csproj"));
            Assert.That(d.IsError, Is.False);
        });
    }

    [Test]
    public void Parse_ErrorLine_MarksDiagnosticAsError()
    {
        const string log = "/repo/src/Foo/Bar.cs(5,1): error GVM003: LINQ usage (System.Linq.Enumerable methods) is prohibited in optimization passes";

        var result = LogParser.Parse(log);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].IsError, Is.True);
        Assert.That(result[0].RuleId, Is.EqualTo("GVM003"));
    }

    [Test]
    public void Parse_LineWithMsBuildProjectPrefix_ParsesSuccessfully()
    {
        const string log = "1>  /repo/src/Foo/Bar.cs(12,34): warning S1234: Some message.";

        var result = LogParser.Parse(log);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].RuleId, Is.EqualTo("S1234"));
    }

    [Test]
    public void Parse_BareWarningWithoutLocation_ParsesSuccessfully()
    {
        const string log = "warning MSB3644: The reference assemblies for .NETFramework were not found.";

        var result = LogParser.Parse(log);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].File, Is.Null);
        Assert.That(result[0].RuleId, Is.EqualTo("MSB3644"));
    }

    [Test]
    public void Parse_DuplicateDiagnosticsFromMultiTargeting_CollapsesToOne()
    {
        var log = "/repo/src/Foo/Bar.cs(12,34): warning S1234: Same message.\n" +
                  "/repo/src/Foo/Bar.cs(12,34): warning S1234: Same message.\n";

        var result = LogParser.Parse(log);

        Assert.That(result, Has.Count.EqualTo(1));
    }

    [Test]
    public void Parse_NonDiagnosticLines_AreIgnored()
    {
        var log = "Build succeeded.\n" +
                  "    0 Warning(s)\n" +
                  "    0 Error(s)\n" +
                  "Time Elapsed 00:00:04.25\n";

        var result = LogParser.Parse(log);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Parse_WindowsStylePath_ParsesSuccessfully()
    {
        const string log = @"C:\repo\src\Foo\Bar.cs(12,34): warning CS0219: The variable 'x' is assigned but its value is never used.";

        var result = LogParser.Parse(log);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].File, Is.EqualTo(@"C:\repo\src\Foo\Bar.cs"));
        Assert.That(result[0].RuleId, Is.EqualTo("CS0219"));
    }
}
