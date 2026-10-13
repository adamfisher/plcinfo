using FluentAssertions;
using PlcInfo.Parsing;
using Xunit.Categories;

namespace PlcInfo.Tests.Integration;

/// <summary>
/// Tests against real TwinCAT 3 projects (see Fixtures/THIRD_PARTY_NOTICES.md).
/// Expected values were read from the files with Python's xml.etree, not taken from plcinfo's output.
/// </summary>
[IntegrationTest]
public class BeckhoffFixtureTests
{
    private static string Fixture(params string[] parts) =>
        Path.Combine(new[] { AppContext.BaseDirectory, "Fixtures", "Beckhoff" }.Concat(parts).ToArray());

    private static string HotConnect(params string[] parts) =>
        Fixture(new[] { "HotConnect", "HotConnectSample_DIPC" }.Concat(parts).ToArray());

    [Fact]
    public async Task HotConnect_Tsproj_ResolvesPlcProjectAndTasks()
    {
        var project = await new TwinCatParser().ParseAsync(HotConnect("HotCOnnectSample_DIPC.tsproj"));

        project.Metadata.Should().NotContainKey("Warnings");
        project.Metadata["PlcProjects"].Should().Be("PLC1");
        project.Controller!.FirmwareRevision.Should().Be("3.1.4024.12");
        project.Controller.Properties["AmsNetId"].Should().Be("5.29.120.74.1.1");

        // Task@CycleTime is in 100 ns ticks: 100000 = 10 ms, 2000000 = 200 ms.
        project.Tasks.Select(t => (t.Name, t.Priority, t.Rate)).Should().Equal(
            ("PlcTask", 20, (double?)10.0), ("VISU_TASK", 30, (double?)200.0));
        project.Tasks[0].ProgramNames.Should().Equal("MAIN");

        project.Programs.Select(p => (p.Name, p.Type)).Should().BeEquivalentTo(new[]
        {
            ("FB_1In", "FunctionBlock"), ("FB_1In_1Out", "FunctionBlock"), ("FB_2In", "FunctionBlock"),
            ("FB_2Out", "FunctionBlock"), ("FB_HandshakeControl", "FunctionBlock"),
            ("I_ConveyerSection", "Interface"), ("MAIN", "Program")
        });
        project.Programs.Single(p => p.Name == "MAIN").TaskName.Should().Be("PlcTask");
    }

    [Fact]
    public async Task HotConnect_Pous_HaveBodiesMethodsAndProperties()
    {
        var project = await new TwinCatParser().ParseAsync(HotConnect("HotCOnnectSample_DIPC.tsproj"));

        var main = project.Programs.Single(p => p.Name == "MAIN");
        main.Language.Should().Be("ST");
        main.LocalTags.Should().HaveCount(33);
        main.LocalTags.Select(t => t.Name).Take(3).Should().Equal("arrFb2In", "arrFb1In1Out", "arrFb2Out");
        main.LocalTags[^1].Name.Should().Be("iCompare");
        main.Routines.Select(r => r.Name).Should().Equal(
            "MAIN", "AddFBtoArray", "checkIoStatus", "Execution", "getFoundIds", "getScannedSlaves",
            "Init_FBs", "Start", "Stop", "updateHMI");
        main.Routines[0].Source.Should().StartWith("CASE nCASE OF");

        var fb = project.Programs.Single(p => p.Name == "FB_1In");
        fb.Properties["Implements"].Should().Be("I_ConveyerSection");
        fb.Routines.Select(r => r.Name).Should().Equal(
            "FB_1In", "DwnStrmHndShkIn.Set", "DwnStrmHndShkOut.Get", "HMI.Get", "Id.Get", "Id.Set", "Pos.Get",
            "Pos.Set", "Sequence", "startSequence", "stopSequence", "UpStrmHndShkIn.Set", "UpStrmHndShkOut.Get");
        fb.Routines.Should().AllSatisfy(r => r.Language.Should().Be("ST"));
        fb.Routines.Single(r => r.Name == "Sequence").Type.Should().Be("Method");
        fb.Routines.Single(r => r.Name == "Id.Get").Source.Should().Be("Id := _mId;");

        // Interface members are declarations only.
        var itf = project.Programs.Single(p => p.Name == "I_ConveyerSection");
        itf.Routines.Should().HaveCount(12);
        itf.Routines.Should().AllSatisfy(r => r.Source.Should().BeNull());
    }

    [Fact]
    public async Task HotConnect_GlobalsAndDataTypes()
    {
        var project = await new TwinCatParser().ParseAsync(HotConnect("HotCOnnectSample_DIPC.tsproj"));

        project.Tags.GroupBy(t => t.Scope).ToDictionary(g => g.Key, g => g.Count()).Should().BeEquivalentTo(
            new Dictionary<string, int> { ["GVL_Constants"] = 6, ["GVL_HMI"] = 11, ["GVL_IO"] = 4 });

        var nPresent = project.Tags.Single(t => t.Name == "nPresent");
        nPresent.DataType.Should().Be("INT");
        nPresent.Description.Should().Be("Number of total Conveyer Groups");

        var nMaxGroups = project.Tags.Single(t => t.Name == "nMaxGroups");
        nMaxGroups.InitialValue.Should().Be("8");
        nMaxGroups.Properties["Constant"].Should().Be("true");

        project.DataTypes.Should().HaveCount(8).And.OnlyContain(d => d.Kind == "Struct");
        var el1008 = project.DataTypes.Single(d => d.Name == "ST_EL1008");
        el1008.Members.Select(m => m.Name).Should().Equal(Enumerable.Range(1, 8).Select(i => $"Input{i}"));
        el1008.Members.Should().OnlyContain(m => m.DataType == "BOOL");
    }

    [Fact]
    public async Task HotConnect_IoTree_NestedBoxesAndLinks()
    {
        var project = await new TwinCatParser().ParseAsync(HotConnect("HotCOnnectSample_DIPC.tsproj"));

        // One EtherCAT device and 23 nested <Box> elements (couplers and their terminals).
        project.IoModules.Should().HaveCount(24);
        project.IoModules[0].Should().Match<PlcInfo.Models.IoModuleInfo>(m =>
            m.Name == "HotConnect Demo(EtherCAT)" && m.Type == "EtherCAT Master" && m.ParentModule == null);

        var coupler = project.IoModules.Single(m => m.Name == "1 In 1 out_1");
        coupler.CatalogNumber.Should().Be("EK1101");
        coupler.Type.Should().Be("Coupler");
        coupler.ParentModule.Should().Be("HotConnect Demo(EtherCAT)");

        var output = project.IoModules.Single(m => m.Name == "Term 19 (EL2008)");
        output.ParentModule.Should().Be("1 In 1 out_1");
        output.CatalogNumber.Should().Be("EL2008");
        output.Type.Should().Be("Digital Output");
        output.Vendor.Should().Be("Beckhoff");
        output.ChannelCount.Should().Be(8);
        output.AssociatedTags.Should().HaveCount(8);
        output.AssociatedTags[0].Should().Be("MAIN.arrFb1In1Out[0].stRack.ST_Out.Output1");

        // 128 Mappings links connect the PLC instance (TIPC) with I/O items (TIID).
        project.Metadata["IoLinks"].Should().Be("128");
        var arrIds = project.Tags.Single(t => t.Name == "arrIds");
        arrIds.Address.Should().Be("%I*");
        arrIds.IsIoTag.Should().BeTrue();
        arrIds.IoModule.Should().NotBeNull();
    }

    [Fact]
    public async Task HotConnect_PlcprojDirectly_UsesTaskObjectInMicroseconds()
    {
        var project = await new TwinCatParser().ParseAsync(HotConnect("PLC1", "PLC1.plcproj"));

        project.Metadata.Should().NotContainKey("Warnings");
        project.Controller!.Name.Should().Be("PLC1");
        project.Controller.FirmwareRevision.Should().Be("3.1.4024.12");
        project.Programs.Should().HaveCount(7);
        project.Tags.Should().HaveCount(21);
        project.DataTypes.Should().HaveCount(8);

        // PlcTask.TcTTO: <CycleTime>10000</CycleTime> is in µs = 10 ms.
        project.Tasks.Should().ContainSingle().Which.Should().Match<PlcInfo.Models.TaskInfo>(t =>
            t.Name == "PlcTask" && t.Rate == 10.0 && t.Priority == 20 && t.ProgramNames.Single() == "MAIN");
    }

    [Fact]
    public async Task TcUnit_TestSuite_Has67MethodsWithSource()
    {
        var project = await new TwinCatParser().ParseAsync(Fixture("TcUnit", "FB_TestSuite.TcPOU"));

        var suite = project.Programs.Should().ContainSingle().Subject;
        suite.Name.Should().Be("FB_TestSuite");
        suite.Type.Should().Be("FunctionBlock");
        suite.LocalTags[0].Name.Should().Be("InstancePath");
        suite.LocalTags[0].DataType.Should().Be("T_MaxString");

        var methods = suite.Routines.Where(r => r.Type == "Method").ToList();
        methods.Should().HaveCount(67);
        methods[0].Name.Should().Be("AddTest");
        methods[^1].Name.Should().Be("SetTestFinished");
        methods.Should().OnlyContain(m => m.Language == "ST" && m.Source != null);

        var assert = methods.Single(m => m.Name == "AssertEquals_BOOL");
        assert.Source.Should().StartWith("IF GVL_TcUnit.IgnoreCurrentTest OR GVL_TcUnit.CurrentTestIsFinished THEN");
        assert.Properties["Declaration"].Should().Contain("METHOD PUBLIC AssertEquals_BOOL");
    }

    [Fact]
    public async Task TcUnit_DataTypes_UnionAndEnumWithBaseType()
    {
        var parser = new TwinCatParser();

        var union = (await parser.ParseAsync(Fixture("TcUnit", "U_ExpectedOrActual.TcDUT"))).DataTypes.Single();
        union.Kind.Should().Be("Union");
        union.Members.Should().HaveCount(23);
        union.Members.Single(m => m.Name == "wstringExpectedOrActual").DataType.Should().Be("WSTRING(255)");
        union.Members[^1].Should().Match<PlcInfo.Models.DataTypeMember>(m =>
            m.Name == "timeofdayExpectedOrActual" && m.DataType == "TIME_OF_DAY");

        var xmlError = (await parser.ParseAsync(Fixture("TcUnit", "E_XmlError.TcDUT"))).DataTypes.Single();
        xmlError.Kind.Should().Be("Enum");
        xmlError.BaseType.Should().Be("BYTE");
        xmlError.Members.Select(m => (m.Name, m.InitialValue)).Should().Equal(
            ("Ok", "0"), ("ErrorMaxBufferLen", "1"), ("ErrorStringLen", "2"), ("Error", "3"));
    }

    [Fact]
    public async Task TF6310_EnumImplicitValuesAndActions()
    {
        var parser = new TwinCatParser();

        var steps = (await parser.ParseAsync(Fixture("TF6310", "E_ClientServerSteps.TcDUT"))).DataTypes.Single();
        steps.Kind.Should().Be("Enum");
        steps.BaseType.Should().BeNull();
        steps.Members.Should().HaveCount(10);
        steps.Members[0].Should().Match<PlcInfo.Models.DataTypeMember>(m => m.Name == "eUDP_STATE_IDLE" && m.InitialValue == "0");
        steps.Members[1].Should().Match<PlcInfo.Models.DataTypeMember>(m => m.Name == "eUDP_STATE_CREATE_START" && m.InitialValue == "1");
        steps.Members[^1].Should().Match<PlcInfo.Models.DataTypeMember>(m => m.Name == "eUDP_STATE_ERROR" && m.InitialValue == "9");

        var fifo = (await parser.ParseAsync(Fixture("TF6310", "FB_FrameFifo.TcPOU"))).Programs.Single();
        fifo.Routines.Select(r => (r.Name, r.Type, r.Language)).Should().Equal(
            ("FB_FrameFifo", "Main", "ST"), ("RemoveHead", "Action", "ST"), ("Clear", "Action", "ST"), ("AddTail", "Action", "ST"));
        fifo.Routines.Single(r => r.Name == "Clear").Source.Should().StartWith("(* Clears all fifo entries *)");

        var bLog = fifo.LocalTags.Single(t => t.Name == "bLog");
        bLog.Usage.Should().Be("Input");
        bLog.InitialValue.Should().Be("FALSE");
        bLog.Description.Should().Be("TRUE => Enable log message output, FALSE => Disable");
        fifo.LocalTags.Single(t => t.Name == "sPrefix").InitialValue.Should().Be("'Unknown::'");
    }
}
