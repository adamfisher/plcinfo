using FluentAssertions;
using PlcInfo.Parsing;
using Xunit.Categories;

namespace PlcInfo.Tests.Integration;

/// <summary>
/// Tests against real TIA Portal Openness (SimaticML) exports and Step 7 sources
/// (see Fixtures/THIRD_PARTY_NOTICES.md). Expected values were read from the files with
/// Python's xml.etree / plain-text parsing, not taken from plcinfo's own output.
/// </summary>
[IntegrationTest]
public class SiemensFixtureTests
{
    private static string Fixture(params string[] parts) =>
        Path.Combine(new[] { AppContext.BaseDirectory, "Fixtures" }.Concat(parts).ToArray());

    private static string Czarnak(params string[] parts) =>
        Fixture(new[] { "Siemens", "simaticml-decoder" }.Concat(parts).ToArray());

    private readonly SiemensTiaPortalParser _tia = new();
    private readonly SiemensStep7Parser _step7 = new();

    [Fact]
    public async Task V21_LadFunctionBlock_NetworksInstructionsAndInterface()
    {
        var path = Czarnak("PLC_1", "Program blocks", "999_MISC", "MotorSoftstart.xml");
        _tia.CanParse(path).Should().BeTrue();

        var project = await _tia.ParseAsync(path);

        project.Vendor.Should().Be("Siemens");
        project.Format.Should().Be("TIA Portal V21 (SimaticML)");
        var block = project.Blocks.Should().ContainSingle().Subject;
        (block.Name, block.Number, block.Type, block.Language).Should().Be(("MotorSoftstart", 8889, "FB", "LAD"));
        block.Properties["MemoryLayout"].Should().Be("Optimized");

        block.NetworkCount.Should().Be(16);
        block.Networks.Select(n => n.Title).Should().Equal(
            "General conditions", "Contactor control", "Control authorisation", "Softstart control",
            "Alarm - circuit protection", "Alarms - contactor", "Alarm - softstart not ON",
            "Alarm - softstart not bypassed", "Alarm - softstart stuck bypassed", "Alarm - softstart not OFF",
            "Alarm - softstart fault", "Any alarm", "Work time counter", "Device state", "Control outputs", null);
        block.Networks.Select(n => n.Instructions.Count).Should().Equal(4, 4, 6, 8, 8, 22, 8, 7, 6, 7, 5, 11, 1, 1, 4, 0);
        block.Networks.SelectMany(n => n.Instructions).GroupBy(i => i.Mnemonic)
            .ToDictionary(g => g.Key, g => g.Count())
            .Should().BeEquivalentTo(new Dictionary<string, int>
            {
                ["Contact"] = 70, ["TON"] = 8, ["Rs"] = 8, ["Coil"] = 7, ["O"] = 7,
                ["TIME_COUNTER_FB"] = 1, ["deviceState"] = 1
            });

        // Interface: Input 11, Output 10, InOut 2, Static 8 top-level members.
        block.Interface.Keys.GroupBy(k => k.Split('.')[0]).ToDictionary(g => g.Key, g => g.Count())
            .Should().BeEquivalentTo(new Dictionary<string, int> { ["Input"] = 11, ["Output"] = 10, ["InOut"] = 2, ["Static"] = 8 });

        // Network 1: Contact(FI_ProfinetOK) -> Contact/neg(FI_EmergencyStop) -> Contact/neg(Alarm) -> Coil(generalConditions).
        block.Networks[0].Text.Should().Be("Coil(#generalConditions) := #FI_ProfinetOK AND NOT #FI_EmergencyStop AND NOT #Alarm");
        block.Networks[0].Instructions.Select(i => (i.Mnemonic, i.Type, string.Join(",", i.Operands))).Should().Equal(
            ("Contact", "Input", "#FI_ProfinetOK"), ("Contact", "Input", "#FI_EmergencyStop"),
            ("Contact", "Input", "#Alarm"), ("Coil", "Output", "#generalConditions"));
        block.Networks[0].Instructions[1].Properties["Negated"].Should().Be("operand");

        // Network 3: the wire out of contact 27 feeds two contacts that meet again at O(in1, in2).
        block.Networks[2].Text.Should().Be(
            "Coil(#Authorisation) := #generalConditions AND #FI_ContactorON AND (#FI_AutoConditions OR #FI_ManualMode)");

        // Network 13: FB call with a multi-instance, parameters in CallInfo declaration order.
        block.Networks[12].Text.Should().Be(
            "\"TIME_COUNTER_FB\"[#C_WORK_TIME](FI_START_COUNTING := #motorConfirmed, FIQ_TIMER := #FIQ_WorkTime)");
        var call = block.Networks[12].Instructions.Single();
        (call.Type, call.Properties["Instance"], call.Properties["BlockType"]).Should().Be(("Call", "#C_WORK_TIME", "FB"));

        // Network 5: TON output Q feeds the Rs set input; the timer instance is a static struct member.
        block.Networks[4].Text.Should().Contain("s1 := TON<Time>[#Timers.CircuitProtection](")
            .And.StartWith("Rs(operand := #FQ_AlarmCircuitProtection, r := ")
            .And.Contain("PT := \"HMI_Settings_DB\".AlarmDelays.Protection).Q");
    }

    [Fact]
    public async Task V21_LadOrganizationBlock_EmptyNetworkAndTitle()
    {
        var project = await _tia.ParseAsync(Czarnak("PLC_1", "Program blocks", "Main.xml"));

        var block = project.Blocks.Should().ContainSingle().Subject;
        (block.Name, block.Number, block.Type, block.Language).Should().Be(("Main", 1, "OB", "LAD"));
        block.Title.Should().Be("\"Main Program Sweep (Cycle)\"");
        block.Properties["SecondaryType"].Should().Be("ProgramCycle");
        block.Interface.Keys.Should().Equal("Input.Initial_Call", "Input.Remanence");
        var network = block.Networks.Should().ContainSingle().Subject;
        network.Text.Should().BeEmpty();
        network.Instructions.Should().BeEmpty();
    }

    [Fact]
    public async Task V21_SclBlock_SourceRebuiltFromTokens()
    {
        var project = await _tia.ParseAsync(Czarnak("Types", "Blocks", "AnalogInput.xml"));

        var block = project.Blocks.Should().ContainSingle().Subject;
        (block.Name, block.Number, block.Type, block.Language).Should().Be(("AnalogInput", 903, "FB", "SCL"));
        block.Interface.Keys.Should().Equal(
            "Input.simulation", "Input.Reset", "Input.analogValue", "Input.simulatedAnalogValue", "Input.Settings",
            "Output.processValue", "Output.wiringAlarm", "Static.statAlarm", "Static.TON_notOK", "Static.TON_OK",
            "Temp.tempRegValue");

        var text = block.Networks.Should().ContainSingle().Subject.Text!;
        var lines = text.Split('\n');
        lines.Should().HaveCount(39);
        lines[0].Should().Be("REGION Choose input");
        lines[10].Should().Be("    #TON_notOK(IN := #Settings.Limits.regMin > #tempRegValue OR #tempRegValue > #Settings.Limits.regMax,");
        lines[31].Should().Be("        #processValue := SCALE_X(MIN := #Settings.Scaling.valueMin, MAX := #Settings.Scaling.valueMax, VALUE := NORM_X(MIN := #Settings.Scaling.regMin, VALUE := #tempRegValue, MAX := #Settings.Scaling.regMax));");
        lines[^1].Should().Be("END_REGION");
        block.Source.Should().Be(text);

        block.Networks[0].Instructions.Select(i => i.Mnemonic).Should().BeEquivalentTo(
            new[] { "#TON_notOK", "#TON_OK", "INT_TO_REAL", "NORM_X", "SCALE_X", "LIMIT" });
    }

    [Fact]
    public async Task V21_GraphBlock_StepsAndTransitions()
    {
        var project = await _tia.ParseAsync(Czarnak("PLC_1", "Program blocks", "300_StateMachine", "StateMachine.xml"));

        var block = project.Blocks.Should().ContainSingle().Subject;
        block.Language.Should().Be("GRAPH");
        block.Properties["GraphSteps"].Should().Be("2");
        block.Properties["GraphTransitions"].Should().Be("2");
        var lines = block.Networks.Single().Text!.Split('\n');
        lines.Should().Contain(new[]
        {
            "STEP 1 Step1 INIT",
            "STEP 2 Step2",
            "TRANSITION 1 Trans1: TrCoil := \"InputValues_DB\".InputsDigital.Start",
            "T2 -> S1 (Jump)"
        });
    }

    [Fact]
    public async Task V21_GlobalDb_MembersBecomeDbScopedTags()
    {
        var project = await _tia.ParseAsync(Czarnak("PLC_1", "Program blocks", "200_Alarms", "Alarms_DB.xml"));

        var block = project.Blocks.Should().ContainSingle().Subject;
        (block.Name, block.Number, block.Type, block.Properties["DbType"]).Should().Be(("Alarms_DB", 201, "DB", "Global"));
        project.Tags.Should().HaveCount(31).And.OnlyContain(t => t.Scope == "Alarms_DB" && t.Usage == "Static");
        project.Tags.Take(4).Select(t => (t.Name, t.DataType)).Should().Equal(
            ("General", "Struct"), ("General.EmergencyStop", "Bool"), ("PLC", "Struct"), ("PLC.SupplyModule2", "Bool"));
    }

    [Fact]
    public async Task V20_GlobalDb_UdtTypedMembersAreNotExpanded()
    {
        var project = await _tia.ParseAsync(Fixture("Siemens", "BlockParam", "v20-real-export-db.xml"));

        project.Format.Should().Be("TIA Portal V20 (SimaticML)");
        project.Blocks.Should().ContainSingle().Which.Name.Should().Be("TP307");
        project.Tags.Select(t => (t.Name, t.DataType)).Should().Equal(
            ("drive1", "Struct"), ("drive1.blocked", "messageConfig_UDT"), ("drive1.communicationError", "messageConfig_UDT"),
            ("drive2", "Struct"), ("drive2.blocked", "messageConfig_UDT"));
    }

    [Fact]
    public async Task PlcDataTypes_MembersWithPathsArraysAndStartValues()
    {
        var settings = (await _tia.ParseAsync(Czarnak("PLC_1", "PLC data types", "UDT_Settings.xml"))).DataTypes.Single();
        settings.Name.Should().Be("UDT_Settings");
        settings.Members.Should().HaveCount(12);
        settings.Members.Single(m => m.Name == "AlarmDelays.Tank").InitialValue.Should().Be("T#5m");
        settings.Members.Single(m => m.Name == "Sensors.LiquidLevel.Tank_1").DataType.Should().Be("AnalogInputSettings");

        var fifo = (await _tia.ParseAsync(Fixture("Siemens", "Smart_Factory", "udt_Fifo.xml"))).DataTypes.Single();
        fifo.Members.Select(m => m.Name).Should().Equal(
            "Entrada", "Entrada.longitud", "Entrada.elemento", "Entrada.inserccion", "Entrada.extraccion", "Entrada.reset",
            "Salidas", "Salidas.elemento", "buffer");
        var buffer = fifo.Members[^1];
        (buffer.DataType, buffer.Dimensions).Should().Be(("Int", "0..31"));
    }

    [Fact]
    public async Task TagTables_AddressesAndIoFlags()
    {
        var inputs = await _tia.ParseAsync(Czarnak("PLC_1", "PLC tags", "Inputs.xml"));
        inputs.Tags.Should().HaveCount(18).And.OnlyContain(t => t.IsIoTag && t.Scope == "Global" && t.Properties["TagTable"] == "Inputs");
        inputs.Tags.Take(3).Select(t => (t.Name, t.DataType, t.Address)).Should().Equal(
            ("DI_EmergencyStop", "Bool", "%I0.0"), ("DI_Reset", "Bool", "%I0.1"), ("DI_ContactorON_Pump1", "Bool", "%I0.4"));

        var outputs = await _tia.ParseAsync(Fixture("Siemens", "Smart_Factory", "DQ.xml"));
        outputs.Tags.Should().HaveCount(28).And.OnlyContain(t => t.IsIoTag && t.Address!.StartsWith("%Q"));
        outputs.Tags[0].Name.Should().Be("out_BeaconGreen");
    }

    [Fact]
    public async Task V18_StlFunction_StatementsAndEdgeTokens()
    {
        var project = await _tia.ParseAsync(Fixture("Siemens", "Smart_Factory", "FC_DI.xml"));

        var block = project.Blocks.Should().ContainSingle().Subject;
        (block.Name, block.Number, block.Type, block.Language).Should().Be(("FC_DI", 1, "FC", "STL"));
        var network = block.Networks.Single();

        // 185 statements: 63 EMPTY_LINE, 1 COMMENT, and 121 operations (A, Assign, Rise, Fall).
        network.Instructions.Should().HaveCount(121);
        network.Instructions.GroupBy(i => i.Mnemonic).ToDictionary(g => g.Key, g => g.Count())
            .Should().BeEquivalentTo(new Dictionary<string, int> { ["A"] = 51, ["="] = 51, ["FP"] = 10, ["FN"] = 9 });
        network.Text!.Split('\n').Take(3).Should().Equal("A \"in_SelStart\"", "= \"DB_DI\".in_SelStart", "");
        network.Text.Should().Contain("//Flacos").And.Contain("FP \"Data\".flanks[0]");
    }

    [Fact]
    public async Task V18_StlFunction_InstructionCalls()
    {
        var project = await _tia.ParseAsync(Fixture("Siemens", "Smart_Factory", "FC_AnalogInCoverter.xml"));

        var network = project.Blocks.Single().Networks.Single();
        network.Text.Should().Contain(
            "CALL NORM_X<Int, Real>(MIN := 0, VALUE := #in_AnalogData, MAX := 27468, Ret_Val := #normData)");
        network.Instructions.Select(i => i.Mnemonic).Should().Equal("CALL", "CALL");
    }

    [Fact]
    public async Task ExportFolder_ParsesEveryObjectRecursively()
    {
        var root = Czarnak();
        _tia.CanParse(root).Should().BeTrue();
        new SiemensStep7Parser().CanParse(root).Should().BeFalse();

        var project = await _tia.ParseAsync(root);

        project.Metadata["ExportFiles"].Should().Be("7");
        project.Metadata.Should().NotContainKey("Warnings");
        // Ordinal path order: PLC_1/PLC data types, PLC_1/PLC tags, PLC_1/Program blocks/..., Types/...
        project.Blocks.Select(b => b.Name).Should().Equal(
            "Alarms_DB", "StateMachine", "MotorSoftstart", "Main", "AnalogInput");
        project.Blocks.Single(b => b.Name == "Main").Properties["File"].Should().Be("PLC_1/Program blocks/Main.xml");
        project.DataTypes.Select(d => d.Name).Should().Equal("UDT_Settings");
        project.Tags.Should().HaveCount(18 + 31);
    }

    [Fact]
    public async Task Step7_AwlSource_NetworksInterfaceAndHeader()
    {
        var path = Fixture("Step7", "planken", "MOTOR.awl");
        _step7.CanParse(path).Should().BeTrue();

        var project = await _step7.ParseAsync(path);

        project.Format.Should().Be("Step 7 AWL source");
        var block = project.Blocks.Should().ContainSingle().Subject;
        (block.Name, block.Type, block.Language, block.Title).Should().Be(("MOTOR v4.3", "FB", "STL", "Motor with CD and SS sensors"));
        block.Properties["Version"].Should().Be("4.3");
        block.Properties["Author"].Should().Be("splanken");
        block.Interface.Keys.GroupBy(k => k.Split('.')[0]).ToDictionary(g => g.Key, g => g.Count())
            .Should().BeEquivalentTo(new Dictionary<string, int> { ["Input"] = 17, ["InOut"] = 8, ["Static"] = 11, ["Temp"] = 1 });
        block.Interface["Static.Off_delay"].Should().Be("TON");

        block.NetworkCount.Should().Be(23);
        block.Networks.Select(n => n.Title).Should().Equal(
            "Check which options to use", "Indicate running", "Off delay", "Off delay OK", "Start delay",
            "Record the last on/off times", "Speed switch time ON", "Speed switch time off", "Speed switch alarm",
            "Speed switch alarm (4B)", "Choke detector time", "CD alarm", "TD time", "TD alarm", "Motor start time",
            "Motor start alarm (secondary)", "Motor start alarm", "General alarm", "OK to start",
            "Energize contactor coil", "Clear alarms", "Store the current running state", "Return ENO");

        var running = block.Networks[1];
        running.Comment.Should().Be("Indicate running if we energized the contactor and received the status feedback\nfrom the contactor.");
        running.Text.Should().Be("A #EQ_M\nA #EQ_MS\n= #Running");
        running.Instructions.Select(i => (i.Mnemonic, i.Operands.Single())).Should().Equal(
            ("A", "#EQ_M"), ("A", "#EQ_MS"), ("=", "#Running"));
    }

    [Fact]
    public async Task Step7_SclSource_FromTiaGenerateSource()
    {
        var path = Fixture("Siemens", "Smart_Factory", "FB_Storer.scl");
        _step7.CanParse(path).Should().BeTrue();

        var project = await _step7.ParseAsync(path);

        project.Format.Should().Be("Step 7 SCL source");
        var block = project.Blocks.Should().ContainSingle().Subject;
        (block.Name, block.Type, block.Language).Should().Be(("FB_Storer", "FB", "SCL"));
        block.Interface.Keys.GroupBy(k => k.Split('.')[0]).ToDictionary(g => g.Key, g => g.Count())
            .Should().BeEquivalentTo(new Dictionary<string, int> { ["Input"] = 3, ["Output"] = 2, ["Static"] = 6, ["Temp"] = 6 });
        block.Interface["Static.Storer"].Should().Be("Array[0..4] of \"udt_StoreRow\"");
        block.Networks.Should().ContainSingle().Which.Text.Should().Contain("IF #in_manual = TRUE THEN");
    }
}
