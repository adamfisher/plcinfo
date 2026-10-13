using FluentAssertions;
using PlcInfo.Models;
using PlcInfo.Parsing;
using PlcInfo.Services;
using Xunit.Categories;

namespace PlcInfo.Tests.Services;

[UnitTest]
public class LogicDiffTests
{
    private readonly PlcDiffService _sut = new();

    // ---- sequence alignment ----

    [Fact]
    public void Align_InsertInMiddle_KeepsFollowingItemsEqual()
    {
        var steps = SequenceAligner.Align(new[] { "a", "b", "c" }, new[] { "a", "x", "b", "c" });

        steps.Select(s => s.Kind).Should().Equal(
            AlignKind.Equal, AlignKind.Insert, AlignKind.Equal, AlignKind.Equal);
    }

    [Fact]
    public void Align_WithSimilarity_PairsChangedItemsAsModify()
    {
        var steps = SequenceAligner.Align(new[] { "a", "b1", "c" }, new[] { "a", "b2", "c" }, (_, _) => 1.0);

        steps.Select(s => s.Kind).Should().Equal(AlignKind.Equal, AlignKind.Modify, AlignKind.Equal);
    }

    [Fact]
    public void Align_DissimilarReplacement_IsDeleteAndInsert()
    {
        var steps = SequenceAligner.Align(new[] { "a", "b" }, new[] { "a", "z" }, (_, _) => 0.0);

        steps.Select(s => s.Kind).Should().Equal(AlignKind.Equal, AlignKind.Delete, AlignKind.Insert);
    }

    // ---- rungs ----

    [Fact]
    public void Rungs_InsertedRung_ReportsOneAdditionNotAShift()
    {
        var before = Ladder("XIC(a)OTE(b);", "XIC(c)OTE(d);", "XIC(e)OTE(f);");
        var after = Ladder("XIC(a)OTE(b);", "XIC(new)OTE(x);", "XIC(c)OTE(d);", "XIC(e)OTE(f);");

        var logic = LogicDetails(before, after);

        logic.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Type = "Added",
            Location = "Programs.Main.Routines.R.Rungs[1]",
            Value2 = "XIC(new)OTE(x);"
        });
    }

    [Fact]
    public void Rungs_ChangedCoil_ReportsModificationWithInstructionSummary()
    {
        var logic = LogicDetails(Ladder("XIC(Start)OTE(Motor);"), Ladder("XIC(Start)OTL(Motor);"));

        var detail = logic.Should().ContainSingle().Subject;
        detail.Type.Should().Be("Modified");
        detail.Message.Should().EndWith("-OTE(Motor) +OTL(Motor)");
        detail.Value1.Should().Be("XIC(Start)OTE(Motor);");
        detail.Value2.Should().Be("XIC(Start)OTL(Motor);");
    }

    [Fact]
    public void Rungs_RemovedRung_ReportsRemovalAtOldNumber()
    {
        var logic = LogicDetails(Ladder("XIC(a)OTE(b);", "XIC(c)OTE(d);"), Ladder("XIC(a)OTE(b);"));

        logic.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Type = "Removed",
            Location = "Programs.Main.Routines.R.Rungs[1]"
        });
    }

    [Fact]
    public void Rungs_WhitespaceOnlyDifference_IsNotAChange()
    {
        var result = _sut.Compare(Ladder("[XIC(a) ,XIO(b) ]OTE(c);"), Ladder("[XIC(a),XIO(b)]OTE(c);"));

        result.HasDifferences.Should().BeFalse();
    }

    [Fact]
    public void Rungs_BranchRearranged_IsReportedAsStructureChange()
    {
        var logic = LogicDetails(Ladder("[XIC(a),XIC(b)]OTE(c);"), Ladder("XIC(a)XIC(b)OTE(c);"));

        logic.Should().ContainSingle().Which.Message.Should().EndWith("branch structure changed");
    }

    [Fact]
    public void Rungs_CommentOnlyChange_IsInformational()
    {
        var before = Ladder("XIC(a)OTE(b);");
        var after = Ladder("XIC(a)OTE(b);");
        after.Programs[0].Routines[0].Rungs[0].Comment = "Start conveyor";

        var result = _sut.Compare(before, after);

        result.HasDifferences.Should().BeFalse();
        result.Details.Should().ContainSingle(d => d.Category == "Logic")
            .Which.Location.Should().Be("Programs.Main.Routines.R.Rungs[0].Comment");
    }

    [Fact]
    public void Rungs_PendingOnlineEdits_AreSkippedWithANote()
    {
        var before = Ladder("XIC(a)OTE(b);");
        var after = Ladder("XIC(a)OTE(b);", "XIC(c)OTE(d);");
        after.Programs[0].Routines[0].Rungs[1].EditState = "e";

        var result = _sut.Compare(before, after);

        result.HasDifferences.Should().BeFalse();
        result.Details.Should().ContainSingle(d => d.Type == "Info" && d.Category == "Logic")
            .Which.Message.Should().Contain("1 edited rung(s) were not compared");
    }

    // ---- routines ----

    [Fact]
    public void Routines_AddedAndRemoved_AreReportedIndividually()
    {
        var before = Project(new ProgramInfo { Name = "Main", Routines = new[] { Routine("A"), Routine("B") } });
        var after = Project(new ProgramInfo { Name = "Main", Routines = new[] { Routine("A"), Routine("C") } });

        var logic = LogicDetails(before, after);

        logic.Select(d => (d.Type, d.Location)).Should().BeEquivalentTo(new[]
        {
            ("Added", "Programs.Main.Routines.C"),
            ("Removed", "Programs.Main.Routines.B")
        });
    }

    // ---- source text ----

    [Fact]
    public void Source_ChangedAndAddedLines_ReportedAsHunksWithLineNumbers()
    {
        var before = StructuredText("a := 1;\nb := 2;\nc := 3;");
        var after = StructuredText("a := 1;\nb := 20;\nc := 3;\nd := 4;");

        var logic = LogicDetails(before, after);

        logic.Select(d => (d.Type, d.Location, d.Value1, d.Value2)).Should().Equal(
            ("Modified", "Programs.Main.Routines.ST.Source[L2]", "b := 2;", "b := 20;"),
            ("Added", "Programs.Main.Routines.ST.Source[L4]", (string?)null, "d := 4;"));
    }

    [Fact]
    public void Source_TrailingWhitespace_IsIgnored()
    {
        var result = _sut.Compare(StructuredText("a := 1;   \nb := 2;"), StructuredText("a := 1;\nb := 2;"));

        result.HasDifferences.Should().BeFalse();
    }

    // ---- networks and blocks ----

    [Fact]
    public void BlockNetworks_ChangedNetwork_IsModified()
    {
        PlcProject Block(string network2) => new()
        {
            Vendor = "Siemens",
            Format = "Test",
            FilePath = "x",
            Blocks = new[]
            {
                new BlockInfo
                {
                    Name = "Main",
                    Type = "OB",
                    Networks = new[]
                    {
                        new NetworkInfo { Number = 1, Text = "A(#Start) =(#Run)" },
                        new NetworkInfo { Number = 2, Text = network2 }
                    }
                }
            }
        };

        var logic = LogicDetails(Block("A(#Run) =(#Lamp)"), Block("A(#Run) AN(#Fault) =(#Lamp)"));

        logic.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Type = "Modified",
            Location = "Blocks.Main.Networks[2]",
            Value2 = "A(#Run) AN(#Fault) =(#Lamp)"
        });
    }

    // ---- data types ----

    [Fact]
    public void DataTypes_MemberChanges_AddedRemovedRetypedAndReordered()
    {
        DataTypeInfo Udt(params (string Name, string Type)[] members) => new()
        {
            Name = "Motor",
            Members = members.Select(m => new DataTypeMember { Name = m.Name, DataType = m.Type }).ToList()
        };
        var before = new PlcProject { Vendor = "T", Format = "T", FilePath = "x", DataTypes = new[] { Udt(("Run", "BOOL"), ("Speed", "INT"), ("Old", "DINT")) } };
        var after = new PlcProject { Vendor = "T", Format = "T", FilePath = "x", DataTypes = new[] { Udt(("Speed", "REAL"), ("Run", "BOOL"), ("New", "DINT")) } };

        var result = _sut.Compare(before, after);

        result.Details.Where(d => d.Category == "DataType").Select(d => (d.Type, d.Location)).Should().BeEquivalentTo(new[]
        {
            ("Added", "DataTypes.Motor.Members.New"),
            ("Removed", "DataTypes.Motor.Members.Old"),
            ("Modified", "DataTypes.Motor.Members.Speed.DataType"),
            ("Modified", "DataTypes.Motor.Members")
        });
    }

    // ---- real export ----

    [Fact]
    public async Task RealExport_TypicalEdits_AreReportedPrecisely()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            var original = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Rockwell", "ACDTestsWithAOI.L5X");
            var edited = Path.Combine(directory, "edited.L5X");
            var text = await File.ReadAllTextAsync(original);
            text = text
                .Replace("XIC(DINT.2)OTE(DINT.3);", "XIC(DINT.2)OTL(DINT.3);")
                .Replace("DINT := DINT AND ULINT; // Line 2 Comment", "DINT := DINT OR ULINT; // Line 2 Comment")
                .Replace("Name=\"TestULINT\" DataType=\"ULINT\"", "Name=\"TestULINT\" DataType=\"LINT\"");
            await File.WriteAllTextAsync(edited, text);

            var parser = new RockwellL5XParser();
            var result = _sut.Compare(await parser.ParseAsync(original), await parser.ParseAsync(edited));

            result.Details.Where(d => d.Type != "Info").Select(d => (d.Type, d.Location)).Should().BeEquivalentTo(new[]
            {
                ("Modified", "Programs.MainProgram.Routines.MainRoutine.Rungs[2]"),
                ("Modified", "Programs.MainProgram.Routines.STRoutine.Source[L2]"),
                ("Modified", "DataTypes.UDT_Test.Members.TestULINT.DataType")
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    // ---- helpers ----

    private List<DiffDetail> LogicDetails(PlcProject before, PlcProject after) =>
        _sut.Compare(before, after).Details.Where(d => d.Category == "Logic" && d.Type != "Info").ToList();

    private static PlcProject Project(params ProgramInfo[] programs) =>
        new() { Vendor = "Test", Format = "Test", FilePath = "x", Programs = programs };

    private static RoutineInfo Routine(string name) => new() { Name = name, Type = "RLL", Language = "RLL" };

    private static PlcProject Ladder(params string[] rungTexts)
    {
        var rungs = rungTexts
            .Select((text, i) => new RungInfo
            {
                Number = i,
                Text = text,
                EditState = "N",
                Instructions = RockwellL5XParser.TokenizeRung(text)
            })
            .ToList();
        return Project(new ProgramInfo
        {
            Name = "Main",
            Routines = new[] { new RoutineInfo { Name = "R", Type = "RLL", Language = "RLL", Rungs = rungs, RungCount = rungs.Count } }
        });
    }

    private static PlcProject StructuredText(string source) => Project(new ProgramInfo
    {
        Name = "Main",
        Routines = new[] { new RoutineInfo { Name = "ST", Type = "ST", Language = "ST", Source = source } }
    });
}
