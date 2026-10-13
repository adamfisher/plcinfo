using FluentAssertions;
using PlcInfo.Models;
using PlcInfo.Parsing;
using PlcInfo.Services;
using Xunit.Categories;

namespace PlcInfo.Tests.Services;

[UnitTest]
public class XrefLintTests
{
    // ---- cross-reference ----

    [Theory]
    [InlineData("Motor.Run[2].0", "Motor")]
    [InlineData("Local:1:I.Data.3", "Local:1:I")]
    [InlineData("#Start", "Start")]
    [InlineData("100", null)]
    [InlineData("?", null)]
    [InlineData("A + B", null)]
    public void BaseTag_StripsMembersAndRejectsLiterals(string operand, string? expected)
    {
        XrefService.BaseTag(operand).Should().Be(expected);
    }

    [Fact]
    public void Xref_ClassifiesReadsAndWritesByInstruction()
    {
        var project = Ladder(new[] { Tag("Start"), Tag("Motor"), Tag("Count"), Tag("Total") },
            "XIC(Start)OTE(Motor);",
            "ADD(Count,1,Total);",
            "MOV(Total,Count);");

        var entries = new XrefService().Build(project);

        entries.Select(e => (e.Tag, e.Access, e.Instruction)).Should().Equal(
            ("Start", "Read", "XIC"), ("Motor", "Write", "OTE"),
            ("Count", "Read", "ADD"), ("Total", "Write", "ADD"),
            ("Total", "Read", "MOV"), ("Count", "Write", "MOV"));
    }

    [Fact]
    public void Xref_ExpressionOperands_ReportKnownTagsAsReads()
    {
        var project = Ladder(new[] { Tag("A"), Tag("B"), Tag("Result") }, "CPT(Result,(A+B)*2);");

        var entries = new XrefService().Build(project);

        entries.Select(e => (e.Tag, e.Access)).Should().Equal(("Result", "Write"), ("A", "Read"), ("B", "Read"));
    }

    [Fact]
    public void Xref_StructuredText_AssignmentTargetIsWriteAndCommentsAreIgnored()
    {
        var project = Project(new[] { Tag("Speed"), Tag("Setpoint"), Tag("Enable") }, new RoutineInfo
        {
            Name = "ST",
            Language = "ST",
            Source = "// Speed := 0; is commented out\nIF Enable THEN\n  Speed := Setpoint * 2; (* Setpoint scaled *)\nEND_IF;"
        });

        var entries = new XrefService().Build(project);

        entries.Select(e => (e.Tag, e.Access, e.Position)).Should().Equal(
            ("Enable", "Read", "Line 2"),
            ("Speed", "Write", "Line 3"),
            ("Setpoint", "Read", "Line 3"));
    }

    [Fact]
    public void Xref_ProgramTagShadowsControllerTag()
    {
        var project = Ladder(new[] { Tag("Run") }, "XIC(Run)OTE(Lamp);");
        project = project with { Tags = new[] { new TagInfo { Name = "Run", Scope = "Controller" }, new TagInfo { Name = "Lamp", Scope = "Controller" } } };

        var entries = new XrefService().Build(project);

        entries.Single(e => e.Tag == "Run").Scope.Should().Be("P");
        entries.Single(e => e.Tag == "Lamp").Scope.Should().Be("Controller");
    }

    // ---- lint ----

    [Fact]
    public void Lint_DuplicateOte_IsError()
    {
        var project = Ladder(new[] { Tag("a"), Tag("b"), Tag("Motor") }, "XIC(a)OTE(Motor);", "XIC(b)OTE(Motor);");

        var finding = Lint(project).Should().ContainSingle(f => f.Code == "PLC001").Subject;

        finding.Severity.Should().Be(LintSeverity.Error);
        finding.Message.Should().Contain("'Motor' is written by OTE in 2 places");
    }

    [Fact]
    public void Lint_LatchUnlatchPair_IsNotADuplicate()
    {
        var project = Ladder(new[] { Tag("a"), Tag("b"), Tag("Motor") }, "XIC(a)OTL(Motor);", "XIC(b)OTU(Motor);");

        Lint(project).Should().NotContain(f => f.Code == "PLC001");
    }

    [Fact]
    public void Lint_MissingAndUnreachableRoutines()
    {
        var program = new ProgramInfo
        {
            Name = "P",
            Properties = new Dictionary<string, string> { ["MainRoutineName"] = "Main" },
            Routines = new[]
            {
                RllRoutine("Main", "JSR(Sub,0);", "JSR(Missing,0);", "FOR(Looped,i,0,10,1);"),
                RllRoutine("Sub", "NOP();"),
                RllRoutine("Looped", "NOP();"),
                RllRoutine("Orphan", "NOP();")
            }
        };

        var findings = Lint(new PlcProject { Programs = new[] { program } });

        findings.Where(f => f.Code == "PLC002").Select(f => f.Message).Should().ContainSingle()
            .Which.Should().Contain("'Missing'");
        findings.Where(f => f.Code == "PLC003").Select(f => f.Location).Should().Equal("P/Orphan");
    }

    [Fact]
    public void Lint_Afi_UnusedTagsAndParametersExcluded()
    {
        var tags = new[]
        {
            Tag("Used"),
            Tag("Unused"),
            new TagInfo { Name = "Param", Scope = "P", Usage = "Input" }
        };
        var project = Ladder(tags, "AFI()XIC(Used)OTE(Used);");

        var findings = Lint(project);

        findings.Should().Contain(f => f.Code == "PLC004" && f.Location == "P/R rung 0");
        findings.Where(f => f.Code == "PLC005").Select(f => f.Location).Should().Equal("P.Unused");
    }

    [Fact]
    public void Lint_PendingEditsAndInhibitedTask()
    {
        var project = Ladder(new[] { Tag("a") }, "XIC(a)OTE(a);");
        project.Programs[0].Routines[0].Rungs[0].EditState = "e";
        project = project with { Tasks = new[] { new TaskInfo { Name = "Slow", IsEnabled = false, ProgramNames = new[] { "P" } } } };

        var codes = Lint(project).Select(f => f.Code).ToList();

        codes.Should().Contain("PLC009").And.Contain("PLC008");
    }

    [Fact]
    public async Task Lint_RealExport_RockwellCicdExampleHasNoErrorsOrWarnings()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Rockwell", "ExampleForCICD_L85E.L5X");
        var project = await new RockwellL5XParser().ParseAsync(path);

        var findings = Lint(project);

        findings.Should().OnlyContain(f => f.Severity == LintSeverity.Info);
        findings.Select(f => f.Location).Should().BeEquivalentTo(
            "Controller.AT_EnableIn", "Controller.AT_ToggleTest", "Controller.test2");
    }

    // ---- helpers ----

    private static IReadOnlyList<LintFinding> Lint(PlcProject project) => new LintService().Analyze(project);

    private static TagInfo Tag(string name) => new() { Name = name, DataType = "BOOL", Scope = "P" };

    private static RoutineInfo RllRoutine(string name, params string[] rungs) => new()
    {
        Name = name,
        Language = "RLL",
        Rungs = rungs.Select((text, i) => new RungInfo
        {
            Number = i,
            Text = text,
            EditState = "N",
            Instructions = RockwellL5XParser.TokenizeRung(text)
        }).ToList()
    };

    private static PlcProject Project(IReadOnlyList<TagInfo> localTags, params RoutineInfo[] routines) => new()
    {
        Vendor = "Test",
        Format = "Test",
        Programs = new[] { new ProgramInfo { Name = "P", LocalTags = localTags, Routines = routines } }
    };

    private static PlcProject Ladder(IReadOnlyList<TagInfo> localTags, params string[] rungs) =>
        Project(localTags, RllRoutine("R", rungs));
}
