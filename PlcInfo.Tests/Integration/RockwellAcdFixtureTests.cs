using FluentAssertions;
using PlcInfo.Models;
using PlcInfo.Parsing;
using Xunit.Categories;

namespace PlcInfo.Tests.Integration;

/// <summary>
/// Tests against the real Studio 5000 v34.01 project ACDTestsWithAOI.ACD and the L5X that was
/// exported from it (hutcheb/acd, Apache-2.0; see Fixtures/THIRD_PARTY_NOTICES.md).
/// Expected values were read independently with Python (xml.etree on the L5X and on the
/// ACD's embedded QuickInfo/TagInfo XML), not taken from plcinfo's output. The cross-check
/// test then requires the ACD result to agree with the L5X result for every supported field.
/// </summary>
[IntegrationTest]
public class RockwellAcdFixtureTests
{
    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Rockwell", name);

    private static Task<PlcProject> ParseAcd() => new RockwellAcdParser().ParseAsync(Fixture("ACDTestsWithAOI.ACD"));

    [Fact]
    public async Task Acd_ControllerAndVersion()
    {
        var project = await ParseAcd();

        project.Format.Should().Be("ACD");
        project.Controller!.Name.Should().Be("ACDTests");
        project.Controller.Type.Should().Be("1756-L85E");
        project.Controller.FirmwareRevision.Should().Be("34.11");
        project.Controller.Properties["Description"].Should().Be("Controller Description");
        project.Metadata["SoftwareRevision"].Should().Be("34.01");
        project.Metadata["LastSavedBuild"].Should().Be("V34.01.00/3843.001");
        project.Metadata["LastSaved"].Should().Be("2024-07-10 20:35:42.296");
    }

    [Fact]
    public async Task Acd_IsMarkedPartialWithOneLineNotice()
    {
        var project = await ParseAcd();

        project.Metadata["ParseStatus"].Should().Be("Partial");
        project.Metadata["Warnings"].Split('\n').Should().Equal(RockwellAcdParser.PartialSupportWarning);
    }

    [Fact]
    public async Task Acd_ProgramsRoutinesAndLadderRungs()
    {
        var project = await ParseAcd();

        project.Tasks.Should().ContainSingle();
        var task = project.Tasks[0];
        (task.Name, task.Type, task.Priority, task.WatchdogTimeout, task.Rate).Should().Be(("MainTask", "CONTINUOUS", 10, 500.0, (double?)null));
        task.ProgramNames.Should().Equal("MainProgram");

        var main = project.Programs.Single(p => p.Name == "MainProgram");
        main.Properties["MainRoutineName"].Should().Be("MainRoutine");
        main.Routines.Select(r => (r.Name, r.Language)).Should().Equal(
            ("FBDRoutine", "FBD"), ("MainRoutine", "RLL"), ("SequentialFunctionChart", "SFC"), ("STRoutine", "ST"));

        var ladder = main.Routines.Single(r => r.Name == "MainRoutine");
        ladder.Rungs.Select(r => (r.Number, r.Text)).Should().Equal(
            (0, "NOP();"), (1, "XIC(DINT.0)OTE(DINT.2);"), (2, "XIC(DINT.2)OTE(DINT.3);"));
        ladder.Rungs[1].Instructions.Select(i => i.Mnemonic).Should().Equal("XIC", "OTE");

        // Non-ladder bodies are not decoded.
        main.Routines.Where(r => r.Language != "RLL").Should().OnlyContain(r => r.Source == null && r.Rungs.Count == 0);

        var aoi = project.Programs.Single(p => p.Type == "AddOnInstruction");
        aoi.Name.Should().Be("AddOnInstruction");
        aoi.Description.Should().Be("AddOnInstruction Description");
        aoi.Routines.Should().ContainSingle().Which.Rungs.Select(r => r.Text).Should().Equal("NOP();");
    }

    [Fact]
    public async Task Acd_TagsDataTypesAndModules()
    {
        var project = await ParseAcd();

        // 21 controller tags in the L5X; "NoAccess" (External Access = None) is not in the ACD tag index.
        project.Tags.Should().HaveCount(20);
        project.Tags.Should().NotContain(t => t.Name == "NoAccess");
        var dint = project.Tags.Single(t => t.Name == "DINT");
        (dint.DataType, dint.Scope, dint.Description).Should().Be(("DINT", "Controller", "DINT_DESCRIPTION"));
        project.Tags.Single(t => t.Name == "ReadOnly").Properties["ExternalAccess"].Should().Be("Read Only");
        project.Tags.Single(t => t.Name == "a5000_String32").DataType.Should().Be("AB:5000_String32_Struct:I:0");

        var main = project.Programs.Single(p => p.Name == "MainProgram");
        main.LocalTags.Should().HaveCount(11); // 12 in the L5X, minus one with External Access = None.
        main.LocalTags.Single(t => t.Name == "MainProgramLocalTag").Description.Should().Be("MainProgramLocalTagDescription");

        var udt = project.DataTypes.Should().ContainSingle().Subject;
        udt.Name.Should().Be("UDT_Test");
        udt.Description.Should().Be("UDT_Desacription");
        udt.Members.Select(m => (m.Name, m.DataType, m.Description)).Should().Equal(
            ("TestDINT", "DINT", "TestDINT Description UDT"),
            ("TestBOOL", "BOOL", "Test Bool Description UDT"),
            ("TestULINT", "ULINT", "Test ULINT Description UDT"));

        project.IoModules.Select(m => (m.Name, m.CatalogNumber, m.Properties["ProductCode"], m.Properties["Major"], m.Properties["Minor"]))
            .Should().Equal(("Local", "1756-L85E", "168", "34", "11"), ("ETH_MODULE", "1756-EN2T", "166", "11", "1"));
    }

    [Fact]
    public async Task Acd_AgreesWithPairedL5xForSupportedFields()
    {
        var acd = await ParseAcd();
        var l5x = await new RockwellL5XParser().ParseAsync(Fixture("ACDTestsWithAOI.L5X"));

        (acd.Controller!.Name, acd.Controller.Type, acd.Controller.FirmwareRevision)
            .Should().Be((l5x.Controller!.Name, l5x.Controller.Type, l5x.Controller.FirmwareRevision));
        acd.Metadata["SoftwareRevision"].Should().Be(l5x.Metadata["SoftwareRevision"]);

        acd.Tasks.Select(t => (t.Name, t.Type, t.Priority, t.Rate, t.WatchdogTimeout, string.Join(",", t.ProgramNames)))
            .Should().Equal(l5x.Tasks.Select(t => (t.Name, t.Type, t.Priority, t.Rate, t.WatchdogTimeout, string.Join(",", t.ProgramNames))));

        acd.Programs.Select(p => (p.Name, p.Type, p.Language)).Should().BeEquivalentTo(l5x.Programs.Select(p => (p.Name, p.Type, p.Language)));
        foreach (var expected in l5x.Programs)
        {
            var actual = acd.Programs.Single(p => p.Name == expected.Name);
            actual.Routines.Select(r => (r.Name, r.Language, r.RungCount)).Should().Equal(expected.Routines.Select(r => (r.Name, r.Language, r.RungCount)));
            foreach (var (a, e) in actual.Routines.Zip(expected.Routines))
            {
                a.Rungs.Select(r => (r.Number, r.Text)).Should().Equal(e.Rungs.Select(r => (r.Number, r.Text)));
                a.Rungs.SelectMany(r => r.Instructions).Select(i => i.Mnemonic + "(" + string.Join(",", i.Operands) + ")")
                    .Should().Equal(e.Rungs.SelectMany(r => r.Instructions).Select(i => i.Mnemonic + "(" + string.Join(",", i.Operands) + ")"));
            }
            if (expected.Type != "AddOnInstruction")
                CompareTags(actual.LocalTags, expected.LocalTags);
        }

        CompareTags(acd.Tags, l5x.Tags);

        acd.DataTypes.Select(d => (d.Name, d.Description, string.Join(";", d.Members.Select(m => $"{m.Name}:{m.DataType}:{m.Dimensions}:{m.Description}"))))
            .Should().Equal(l5x.DataTypes.Select(d => (d.Name, d.Description?.Trim(), string.Join(";", d.Members.Select(m => $"{m.Name}:{m.DataType}:{m.Dimensions}:{m.Description}")))));

        acd.IoModules.Select(m => (m.Name, m.CatalogNumber, m.Type, m.Vendor, m.Properties["Major"], m.Properties["Minor"], m.Properties["ProductCode"], m.Properties["ProductType"]))
            .Should().Equal(l5x.IoModules.Select(m => (m.Name, m.CatalogNumber, m.Type, m.Vendor, m.Properties["Major"], m.Properties["Minor"], m.Properties["ProductCode"], m.Properties["ProductType"])));
    }

    /// <summary>
    /// Every tag in the ACD index must match the L5X. Known, documented gaps: tags with
    /// External Access "None" are not in the index, and alias tags carry the alias target's
    /// resolved data type instead of an AliasFor reference.
    /// </summary>
    private static void CompareTags(IReadOnlyList<TagInfo> acd, IReadOnlyList<TagInfo> l5x)
    {
        var expected = l5x.Where(t => t.Properties.GetValueOrDefault("ExternalAccess") != "None").ToList();
        acd.Select(t => t.Name).Should().Equal(expected.Select(t => t.Name));

        foreach (var (a, e) in acd.Zip(expected))
        {
            if (e.AliasFor is null)
                a.DataType.Should().Be(e.DataType, $"tag {e.Name}");
            a.Scope.Should().Be(e.Scope);
            a.Description.Should().Be(e.Description?.Trim() is { Length: > 0 } d ? d : null, $"tag {e.Name}");
            a.ArrayDimensions.Should().BeEquivalentTo(e.ArrayDimensions);
            a.Properties["ExternalAccess"].Should().Be(e.Properties["ExternalAccess"]);
        }
    }
}
