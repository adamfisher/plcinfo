using FluentAssertions;
using PlcInfo.Models;
using PlcInfo.Parsing;
using PlcInfo.Services;
using Xunit.Categories;

namespace PlcInfo.Tests.Integration;

/// <summary>
/// Tests against real PLCopen XML exports (see Fixtures/THIRD_PARTY_NOTICES.md) and one hand-written
/// sample. Expected values were read from the files with Python's xml.etree, not taken from plcinfo's
/// output. Network text uses plcinfo's canonical form, so those expectations restate the operands
/// found in the file in that form.
/// </summary>
[IntegrationTest]
public class PLCopenFixtureTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "PLCopen", name);

    private static async Task<PlcProject> ParseAsync(string name)
    {
        var parser = new CodesysParser();
        parser.CanParse(Fixture(name)).Should().BeTrue();
        return await parser.ParseAsync(Fixture(name));
    }

    [Fact]
    public async Task Codesys_v35SP4_LibraryExport_FunctionBlocksMethodsDutsAndGvl()
    {
        var project = await ParseAsync("OSC_MQTT.xml");

        project.Vendor.Should().Be("CODESYS");
        project.Format.Should().Be("PLCopen XML (tc6_0200)");
        project.Metadata["ProductVersion"].Should().Be("CODESYS V3.5 SP4 Patch 1");
        project.Metadata.Should().NotContainKey("Warnings");
        project.Tasks.Should().BeEmpty("a library export has no task configuration");

        // DUTs are data types, not programs.
        project.Programs.Select(p => (p.Name, p.Type, p.Language)).Should().Equal(
            ("MQTTClient", "FunctionBlock", "ST"), ("MQTTPub", "FunctionBlock", "ST"));
        project.DataTypes.Select(d => (d.Name, d.Kind, d.BaseType)).Should().Equal(
            ("CLIENTSTATE", "Enum", (string?)null),
            ("MESSAGETYPE", "Enum", "BYTE"),
            ("MQTTMESSAGE", "Struct", (string?)null),
            ("QoS", "Enum", "BYTE"));
        project.DataTypes[1].Members[0].Should().BeEquivalentTo(new { Name = "CONNECT", InitialValue = "16#10" });
        project.DataTypes[3].Members.Select(m => (m.Name, m.InitialValue)).Should().Equal(
            ("AtMostOnce", "2#0"), ("AtLeastOnce", "2#10"), ("ExactlyOnce", "2#100"));
        project.DataTypes[2].Members.Should().HaveCount(6);

        var client = project.Programs[0];
        client.Routines.Where(r => r.Type == "Method").Select(r => r.Name).Should().Equal(
            "_writeByte", "_connect", "_write", "_writeString", "_writeWord", "_disconnect",
            "Publish", "Subscribe", "_Send", "_Receive", "_Keepalive");
        client.Routines.Should().OnlyContain(r => r.Language == "ST" && r.Source != null);
        client.LocalTags.Single(t => t.Name == "RBUF").DataType.Should().Be("ARRAY[0..Constants.MQTT_MAX_PACKET_SIZE] OF BYTE");

        project.Programs[1].Routines.Single().Source.Should().Be(
            "IF xExecute THEN\n\tpClient^.Publish(sTopic:=sTopic, sValue:=sValue, bQos:=0, xRetain:=FALSE, xExecute:=TRUE, xDone => xDone);\nEND_IF");

        // The GVL lives in the project's addData.
        project.Tags.Select(t => (t.Name, t.DataType, t.InitialValue)).Should().Equal(
            ("MQTTPROTOCOLVERSION", "BYTE", "BYTE#3"),
            ("MQTT_MAX_PACKET_SIZE", "BYTE", "128"),
            ("MQTT_MIN_KEEPALIVE", "TIME", "TIME#15s0ms"),
            ("MQTT_DEFAULT_PORT", "UINT", "1883"),
            ("MQTT_MAX_CALLBACKS", "INT", "3"));
        project.Tags.Should().OnlyContain(t => t.Scope == "Constants" && t.Properties["Constant"] == "true");
    }

    [Fact]
    public async Task Codesys_v35SP15_ProjectExport_TaskIsoIntervalResourcePousAndDut()
    {
        var project = await ParseAsync("robin_codesys_project.xml");

        var task = project.Tasks.Should().ContainSingle().Subject;
        task.Name.Should().Be("MainTask");
        task.Priority.Should().Be(1);
        task.Rate.Should().Be(10); // interval="PT0.01S"
        task.ProgramNames.Should().Equal("MAIN", "ROS_PRG");

        project.Programs.Select(p => (p.Name, p.Type, p.TaskName)).Should().Equal(
            ("ROS_PRG", "Program", "MainTask"), ("MAIN", "Program", "MainTask"));
        project.Programs[0].LocalTags.Should().HaveCount(15);

        var dut = project.DataTypes.Should().ContainSingle().Subject;
        dut.Name.Should().Be("TestStruct");
        dut.Kind.Should().Be("Struct");
        dut.Members.Select(m => (m.Name, m.DataType)).Should().Equal(
            ("var_bool", "BOOL"), ("var_byte", "BYTE"), ("var_int16", "INT"), ("var_uint64", "ULINT"),
            ("var_float32", "REAL"), ("var_float64", "LREAL"), ("var_string", "STRING"), ("var_pose", "Pose"),
            ("var_struct_array", "ARRAY[1..2] OF ByteMultiArray"), ("var_struct_varlen_array", "ARRAY[1..2] OF ByteMultiArray"));
        dut.Members[8].Dimensions.Should().Be("1..2");

        project.Controller!.FirmwareRevision.Should().Be("3.5.15.0");
        project.Controller.Properties["DeviceType"].Should().Be("4102");
    }

    [Fact]
    public async Task Codesys_v35SP20_ProjectExport_PropertiesInterfacesInheritanceAndGvl()
    {
        var project = await ParseAsync("CODESYS_HashTable_Struct_Example.xml");

        project.Tasks.Select(t => (t.Name, t.Rate, t.Priority)).Should().Equal(
            ("MainTask", (double?)20, 1), ("VISU_TASK", (double?)100, 31));
        project.Tasks[0].ProgramNames.Should().Equal("PRG_ProductSetting");

        project.Programs.Select(p => (p.Name, p.Type)).Should().Equal(
            ("PRG_ProductSetting", "Program"),
            ("FB_Product", "FunctionBlock"),
            ("FB_ProductElement", "FunctionBlock"),
            ("GetIdFromProductName", "Function"),
            ("FB_ProductElementFactory", "FunctionBlock"),
            ("IProductElement", "Interface"));

        var element = project.Programs.Single(p => p.Name == "FB_ProductElement");
        element.Properties.Should().Contain("Extends", "FBF.InstanceBase").And.Contain("Implements", "IProductElement");
        element.Routines.Where(r => r.Type == "Method").Select(r => r.Name).Should().Equal(
            "ElementEquals", "ElementHashCode", "ElementCompareTo", "prvInstInit");
        element.Routines.Single(r => r.Name == "ProductValue.Get").Source.Should().Be("ProductValue := _product;\n");
        element.Routines.Single(r => r.Name == "ProductValue.Set").Source.Should().Be("_product := ProductValue;\n");

        project.Programs.Single(p => p.Name == "PRG_ProductSetting").Routines.Count(r => r.Type == "Method").Should().Be(11);
        project.Programs.Single(p => p.Name == "FB_ProductElementFactory").Routines
            .Where(r => r.Type == "Property").Select(r => r.Name)
            .Should().Equal("prvInstCount.Get", "prvInstPoolExtendsFactor.Get", "prvInstSize.Get");

        var itf = project.Programs.Single(p => p.Type == "Interface");
        itf.Properties.Should().Contain("Extends", "COL.IElement");
        itf.Routines.Single().Name.Should().Be("ProductValue");

        project.DataTypes.Select(d => (d.Name, d.Kind)).Should().Equal(("ST_Product", "Struct"), ("E_ProductSettingState", "Enum"));
        project.DataTypes[0].Members.Select(m => m.Name).Should().Equal("id", "name", "width", "length", "height");
        project.DataTypes[1].Members.Select(m => (m.Name, m.InitialValue)).Should().Equal(
            ("Initial", "0"), ("Create", (string?)null), ("Edit", (string?)null));

        var gvl = project.Tags.Should().ContainSingle().Subject;
        gvl.Should().BeEquivalentTo(new { Name = "MAX_PRODUCT_NUM", Scope = "GVL", InitialValue = "100" }, o => o.ExcludingMissingMembers());
        gvl.Properties["Constant"].Should().Be("true");
    }

    [Fact]
    public async Task Codesys_v35SP18_GraphicalBodiesEventTasksAndSubranges()
    {
        var project = await ParseAsync("codesys_code_execution_speedTests.xml");

        // Two device configurations with 18 tasks each.
        project.Tasks.Should().HaveCount(36);
        project.Tasks.Select(t => t.Properties["Configuration"]).Distinct().Should().Equal(
            "CODESYS_Control_Win_V3_x64", "CODESYS_Control_RTE_V3_x64");
        var x01 = project.Tasks.First(t => t.Name == "x01");
        x01.Type.Should().Be("Event");
        x01.Rate.Should().BeNull();
        x01.Properties["Single"].Should().Be("x01_ImpactOfDynamicArrays.vStart");
        project.Tasks.First(t => t.Name == "SpdRnnr").Rate.Should().Be(200);

        Network("addTwoIntsLadder", "LD").Text.Should().Be("addTwoIntsLadder := ADD(EN := XIC(TRUE), In2 := Int1, In3 := Int2)");
        Network("addTwoIntsLadder", "LD").Instructions.Select(i => i.Mnemonic).Should().Equal("XIC", "ADD");
        Network("addTwoIntsFBD", "FBD").Text.Should().Be("addTwoIntsFBD := ADD(In1 := int1, In2 := int2)");
        Network("addTwoIntsCFC", "CFC").Text.Should().Be("addTwoIntsCFC := ADD(In1 := int1, In2 := int2)");

        project.Programs.Single(p => p.Name == "x07_StringOperations").LocalTags.Single(t => t.Name == "cNrRuns")
            .DataType.Should().Be("USINT(0..9)");
        project.Metadata.Should().NotContainKey("Warnings");

        NetworkInfo Network(string pou, string language)
        {
            var routine = project.Programs.Single(p => p.Name == pou).Routines.Single();
            routine.Language.Should().Be(language);
            return routine.Networks.Should().ContainSingle().Subject;
        }
    }

    [Fact]
    public async Task OpenPlcEditor_LadderExport_IsRecognisedWithoutCodesysMarkers()
    {
        var project = await ParseAsync("OpenPLC_AND_plc.xml");

        project.Vendor.Should().Be("PLCopen");
        project.Format.Should().Be("PLCopen XML (tc6_0201)");
        project.Tasks.Single().Should().BeEquivalentTo(new { Name = "task0", Rate = 20.0, Priority = 0 }, o => o.ExcludingMissingMembers());
        project.Tasks.Single().ProgramNames.Should().Equal("program0");

        var program = project.Programs.Single();
        program.Language.Should().Be("LD");
        program.TaskName.Should().Be("task0");
        var network = program.Routines.Single().Networks.Single();
        network.Text.Should().Be("XIC(X100) XIC(X101) OTE(Y100)");
        network.Instructions.Select(i => (i.Mnemonic, i.Operands.Single())).Should().Equal(
            ("XIC", "X100"), ("XIC", "X101"), ("OTE", "Y100"));
    }

    [Fact]
    public async Task HandWritten_LadderFbdAndSfc()
    {
        // Hand-written, schema-valid sample (see the comment in the file); not a tool export.
        var project = await ParseAsync("HandWritten_LD_FBD_SFC.xml");

        var ladder = project.Programs.Single(p => p.Name == "Ladder").Routines.Single();
        ladder.Networks.Select(n => n.Text).Should().Equal(
            "XIC(a) [XIC(b) , XIO(c)] OTE(d)",
            "tmr(IN := XICP(start), PT := T#5s)\ntmr.Q OTL(done)",
            "XIO(stop) JMP(End)",
            "RET");
        ladder.Networks[0].Comment.Should().Be("Motor permissive");
        ladder.Networks[3].Properties["Label"].Should().Be("End");
        ladder.Networks[1].Instructions.Select(i => i.Mnemonic).Should().Equal("XICP", "TON", "OTL");

        project.Programs.Single(p => p.Name == "Calc").Routines.Single().Networks.Single().Text
            .Should().Be("Calc := SEL(G := NOT(enable), IN0 := ADD(IN1 := x, IN2 := 1), IN1 := x)");

        var sfc = project.Programs.Single(p => p.Name == "Sequence").Routines.First();
        sfc.Language.Should().Be("SFC");
        sfc.Networks.Single().Text.Should().Be(string.Join("\n",
            "INITIAL STEP Init",
            "  ACTION P1 Fill",
            "TRANSITION Init -> Right WHEN go",
            "TRANSITION Init -> Left WHEN left AND NOT go PRIORITY 2",
            "STEP Left",
            "STEP Right",
            "TRANSITION Right -> JUMP(Init) WHEN NOT(left)",
            "TRANSITION Left -> JUMP(Init) WHEN go"));
        sfc.Properties["StepCount"].Should().Be("3");

        project.Tasks.Select(t => (t.Name, t.Type, t.Rate)).Should().Equal(
            ("Fast", "Periodic", (double?)1500), ("Micro", "Periodic", (double?)0.1), ("OnEdge", "Event", (double?)null));
        project.DataTypes.Select(d => (d.Name, d.BaseType)).Should().Equal(("Range", "INT(-10..10)"), ("PInt", "POINTER TO INT"));
        project.Tags.Single(t => t.Name == "LIMITS").Should().BeEquivalentTo(
            new { DataType = "ARRAY[1..N_MAX] OF LREAL", InitialValue = "[1.5, 3(0.0)]", Scope = "Limits" }, o => o.ExcludingMissingMembers());
        project.Tags.Single(t => t.Name == "X0").IsIoTag.Should().BeTrue();
    }

    [Theory]
    [InlineData("codesys_code_execution_speedTests.xml")]
    [InlineData("HandWritten_LD_FBD_SFC.xml")]
    public async Task SameFileDiffedWithItself_HasNoDifferences(string name)
    {
        var parser = new CodesysParser();

        var result = new PlcDiffService().Compare(await parser.ParseAsync(Fixture(name)), await parser.ParseAsync(Fixture(name)));

        result.HasDifferences.Should().BeFalse();
    }
}
