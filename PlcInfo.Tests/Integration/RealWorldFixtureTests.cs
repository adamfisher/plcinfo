using FluentAssertions;
using PlcInfo.Parsing;
using PlcInfo.Services;
using Xunit.Categories;

namespace PlcInfo.Tests.Integration;

/// <summary>
/// Tests against real, tool-exported project files (see Fixtures/THIRD_PARTY_NOTICES.md).
/// Expected values were read from the files with an independent XML parser,
/// not taken from plcinfo's own output.
/// </summary>
[IntegrationTest]
public class RealWorldFixtureTests
{
    private static string Fixture(params string[] parts) =>
        Path.Combine(new[] { AppContext.BaseDirectory, "Fixtures" }.Concat(parts).ToArray());

    [Fact]
    public async Task Studio5000_v34_AllRoutineLanguagesAndAoi()
    {
        var project = await new RockwellL5XParser().ParseAsync(Fixture("Rockwell", "ACDTestsWithAOI.L5X"));

        project.Controller!.Name.Should().Be("ACDTests");
        project.Controller.Type.Should().Be("1756-L85E");
        project.Controller.FirmwareRevision.Should().Be("34.11");

        project.Tasks.Should().ContainSingle();
        project.Tasks[0].Name.Should().Be("MainTask");
        project.Tasks[0].ProgramNames.Should().Equal("MainProgram");

        var main = project.Programs.Single(p => p.Name == "MainProgram");
        main.LocalTags.Should().HaveCount(12);
        main.Routines.Select(r => (r.Name, r.Language)).Should().BeEquivalentTo(new[]
        {
            ("FBDRoutine", "FBD"),
            ("MainRoutine", "RLL"),
            ("SequentialFunctionChart", "SFC"),
            ("STRoutine", "ST")
        });

        var ladder = main.Routines.Single(r => r.Name == "MainRoutine");
        ladder.RungCount.Should().Be(3);
        ladder.Rungs[1].Instructions.Select(i => i.Mnemonic).Should().Equal("XIC", "OTE");
        ladder.Rungs[1].Instructions[0].Operands.Should().Equal("DINT.0");
        ladder.Rungs[1].Instructions[1].Operands.Should().Equal("DINT.2");

        var aoi = project.Programs.Single(p => p.Type == "AddOnInstruction");
        aoi.Name.Should().Be("AddOnInstruction");
        aoi.LocalTags.Should().HaveCount(2);

        project.Tags.Should().HaveCount(21);
        project.IoModules.Select(m => m.Name).Should().Equal("Local", "ETH_MODULE");
    }

    [Fact]
    public async Task Studio5000_v36_RockwellCicdExample_TasksBranchesAndJsr()
    {
        var project = await new RockwellL5XParser().ParseAsync(Fixture("Rockwell", "ExampleForCICD_L85E.L5X"));

        project.Controller!.Name.Should().Be("ExampleWithCICD_L85E");
        project.Tasks.Select(t => (t.Name, t.Type)).Should().Equal(
            ("T00_MiscLogic", "CONTINUOUS"),
            ("T01_ShowcaseDataTypes", "PERIODIC"));
        project.Tasks[1].Rate.Should().Be(10);
        project.Tasks[0].ProgramNames.Should().Equal("P00_AND_OR_Logic", "P01_ClearFault");

        project.Programs.Should().HaveCount(4);
        project.Programs.SelectMany(p => p.Routines).Sum(r => r.RungCount).Should().Be(13);

        var main = project.Programs.Single(p => p.Name == "P00_AND_OR_Logic").Routines.Single(r => r.Name == "R00_Main");
        main.Rungs[0].Instructions.Single().Mnemonic.Should().Be("JSR");
        main.Rungs[0].Instructions.Single().Operands.Should().Equal("R01_AND_Example", "0");

        var orRung = project.Programs.Single(p => p.Name == "P00_AND_OR_Logic")
            .Routines.Single(r => r.Name == "R02_OR_Example").Rungs.Single();
        orRung.Instructions.Select(i => i.Mnemonic).Should().Equal("XIC", "XIC", "OTE");
        orRung.Instructions[1].Operands.Should().Equal("I_OR_2");
    }

    [Fact]
    public async Task Studio5000_SameFileDiffedWithItself_HasNoDifferences()
    {
        var path = Fixture("Rockwell", "ExampleForCICD_L85E.L5X");
        var parser = new RockwellL5XParser();

        var result = new PlcDiffService().Compare(await parser.ParseAsync(path), await parser.ParseAsync(path));

        result.HasDifferences.Should().BeFalse();
    }

    [Fact]
    public async Task Codesys_v35_PlcOpenExport_FunctionBlocksVariablesAndMethods()
    {
        var path = Fixture("PLCopen", "OSC_MQTT.xml");
        var parser = new CodesysParser();

        parser.CanParse(path).Should().BeTrue();
        var project = await parser.ParseAsync(path);

        var pous = project.Programs.Where(p => p.Type == "functionBlock").ToList();
        pous.Select(p => p.Name).Should().BeEquivalentTo("MQTTClient", "MQTTPub");

        var client = pous.Single(p => p.Name == "MQTTClient");
        client.LocalTags.Select(t => t.Name).Should().Contain(new[] { "sServerIP", "uiPort", "xEnable" });
        client.Routines.Should().Contain(r => r.Name == "Publish");
    }
}
