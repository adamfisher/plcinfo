using System.Globalization;
using System.Text;
using FluentAssertions;
using PlcInfo.Models;
using PlcInfo.Parsing;
using PlcInfo.Parsing.PlcOpen;
using Xunit.Categories;

namespace PlcInfo.Tests.Parsing;

/// <summary>
/// PLCopen TC6 XML details: types, DUTs, tasks, graphical bodies and CODESYS addData.
/// </summary>
[UnitTest]
public class PlcOpenParserTests : IDisposable
{
    private const string Ns = "http://www.plcopen.org/xml/tc6_0201";
    private const string Cds = "http://www.3s-software.com/plcopenxml/";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "plcopen_" + Guid.NewGuid().ToString("N"));

    public PlcOpenParserTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private async Task<PlcProject> ParseAsync(string types, string instances = "<configurations/>", string addData = "", string ns = Ns)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".xml");
        await File.WriteAllTextAsync(path, $@"<?xml version='1.0' encoding='utf-8'?>
<project xmlns='{ns}' xmlns:xhtml='http://www.w3.org/1999/xhtml'>
  <fileHeader companyName='' productName='CODESYS' productVersion='CODESYS V3.5 SP19' creationDateTime='2024-01-01T00:00:00'/>
  <contentHeader name='Test'><coordinateInfo><fbd><scaling x='1' y='1'/></fbd><ld><scaling x='1' y='1'/></ld><sfc><scaling x='1' y='1'/></sfc></coordinateInfo></contentHeader>
  <types>{types}</types>
  <instances>{instances}</instances>
  {(addData.Length > 0 ? $"<addData>{addData}</addData>" : string.Empty)}
</project>");
        return await new CodesysParser().ParseAsync(path);
    }

    private static string Pou(string name, string body, string pouType = "program", string iface = "", string extra = "") =>
        $"<dataTypes/><pous><pou name='{name}' pouType='{pouType}'><interface>{iface}</interface><body>{body}</body>{extra}</pou></pous>";

    private static string Var(string name, string type) => $"<variable name='{name}'><type>{type}</type></variable>";

    // ---------------------------------------------------------------- detection

    [Theory]
    [InlineData("http://www.plcopen.org/xml/tc6_0200", true)]
    [InlineData("http://www.plcopen.org/xml/tc6_0201", true)]
    [InlineData("http://www.plcopen.org/xml/tc6.xsd", false)]
    [InlineData("www.iec.ch/public/TC65SC65BWG7TF10", false)]
    public void CanParse_SniffsRootProjectElementNamespace(string ns, bool expected)
    {
        var path = Path.Combine(_dir, "plc.xml");
        // Beremiz writes no "codesys" anywhere; the root element decides.
        File.WriteAllText(path, $"<?xml version='1.0'?>\n<project xmlns='{ns}'><fileHeader companyName='Beremiz' productName='Beremiz'/></project>");

        new CodesysParser().CanParse(path).Should().Be(expected);
    }

    [Fact]
    public void CanParse_OtherXmlMentioningCodesys_IsNotClaimed()
    {
        var path = Path.Combine(_dir, "notes.xml");
        File.WriteAllText(path, "<?xml version='1.0'?><notes>exported from CODESYS</notes>");

        new CodesysParser().CanParse(path).Should().BeFalse();
    }

    [Theory]
    [InlineData("Machine.project")]
    [InlineData("Utilities.library")]
    public async Task ParseAsync_BinaryCodesysFile_ThrowsNotSupportedWithExportAdvice(string fileName)
    {
        var path = Path.Combine(_dir, fileName);
        await File.WriteAllBytesAsync(path, new byte[] { 0x00, 0x01, 0xFA, 0xCE, 0x50, 0x4B, 0x03, 0x04 });

        var parser = new CodesysParser();
        parser.CanParse(path).Should().BeTrue();
        var act = () => parser.ParseAsync(path);

        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*Export PLCopenXML*");
    }

    [Fact]
    public async Task ParseAsync_XmlThatIsNotPlcOpen_ThrowsNotSupported()
    {
        var path = Path.Combine(_dir, "native.export");
        await File.WriteAllTextAsync(path, "<?xml version='1.0'?><ExportFile><StructuredView/></ExportFile>");

        var act = () => new CodesysParser().ParseAsync(path);

        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*not a PLCopen XML*");
    }

    [Fact]
    public async Task ParseAsync_Dtd_IsRejected()
    {
        var path = Path.Combine(_dir, "dtd.xml");
        await File.WriteAllTextAsync(path, $"<?xml version='1.0'?><!DOCTYPE project [<!ENTITY x 'y'>]><project xmlns='{Ns}'/>");

        var act = () => new CodesysParser().ParseAsync(path);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---------------------------------------------------------------- types

    [Fact]
    public async Task Variables_ElementaryArrayPointerSubrangeAndStringTypes_AreSpelledInIecSyntax()
    {
        var iface = "<localVars>" +
                    Var("t", "<TIME/>") + Var("r", "<LREAL/>") + Var("s", "<string length='80'/>") + Var("w", "<wstring/>") +
                    Var("a", "<array><dimension lower='1' upper='MAX_N'/><dimension lower='-2' upper='2'/><baseType><derived name='ST_Item'/></baseType></array>") +
                    Var("n", "<array><dimension lower='0' upper='9'/><baseType><INT/></baseType></array>") +
                    Var("p", "<pointer><baseType><array><dimension lower='0' upper='3'/><baseType><BYTE/></baseType></array></baseType></pointer>") +
                    Var("sr", "<subrangeSigned><range lower='-5' upper='5'/><baseType><SINT/></baseType></subrangeSigned>") +
                    "</localVars>";

        var project = await ParseAsync(Pou("Main", "<ST><xhtml xmlns='http://www.w3.org/1999/xhtml'/></ST>", iface: iface));

        var tags = project.Programs.Single().LocalTags;
        tags.Select(t => (t.Name, t.DataType)).Should().Equal(
            ("t", "TIME"), ("r", "LREAL"), ("s", "STRING(80)"), ("w", "WSTRING"),
            ("a", "ARRAY[1..MAX_N, -2..2] OF ST_Item"),
            ("n", "ARRAY[0..9] OF INT"),
            ("p", "POINTER TO ARRAY[0..3] OF BYTE"),
            ("sr", "SINT(-5..5)"));
        tags.Single(t => t.Name == "n").ArrayDimensions.Should().Equal(10);
        tags.Single(t => t.Name == "a").ArrayDimensions.Should().BeNull("a bound is a constant name");
        tags.Should().OnlyContain(t => t.Usage == "Local" && t.Scope == "Main");
    }

    [Fact]
    public async Task Variables_InitialValuesQualifiersAndAddresses_AreRead()
    {
        var iface = "<inputVars retain='true'>" +
                    "<variable name='x' address='%IX0.1'><type><BOOL/></type><initialValue><simpleValue value='TRUE'/></initialValue>" +
                    "<documentation><xhtml xmlns='http://www.w3.org/1999/xhtml'> Start button </xhtml></documentation></variable>" +
                    "<variable name='arr'><type><array><dimension lower='0' upper='3'/><baseType><INT/></baseType></array></type>" +
                    "<initialValue><arrayValue><value><simpleValue value='1'/></value><value repetitionValue='3'><simpleValue value='0'/></value></arrayValue></initialValue></variable>" +
                    "<variable name='pt'><type><derived name='POINT'/></type>" +
                    "<initialValue><structValue><value member='x'><simpleValue value='1'/></value><value member='y'><simpleValue value='2'/></value></structValue></initialValue></variable>" +
                    "</inputVars>";

        var project = await ParseAsync(Pou("FB", "<ST><xhtml xmlns='http://www.w3.org/1999/xhtml'/></ST>", "functionBlock", iface));

        var tags = project.Programs.Single().LocalTags;
        var x = tags.Single(t => t.Name == "x");
        x.Usage.Should().Be("Input");
        x.InitialValue.Should().Be("TRUE");
        x.Address.Should().Be("%IX0.1");
        x.IsIoTag.Should().BeTrue();
        x.Description.Should().Be("Start button");
        x.Properties.Should().Contain("Retain", "true");
        tags.Single(t => t.Name == "arr").InitialValue.Should().Be("[1, 3(0)]");
        tags.Single(t => t.Name == "pt").InitialValue.Should().Be("(x := 1, y := 2)");
    }

    [Fact]
    public async Task DataTypes_AreDataTypesNotPrograms()
    {
        const string types = @"<dataTypes>
  <dataType name='ST_Motor'><baseType><struct>
    <variable name='speed'><type><REAL/></type><initialValue><simpleValue value='1.5'/></initialValue></variable>
    <variable name='buf'><type><array><dimension lower='1' upper='N'/><baseType><BYTE/></baseType></array></type></variable>
  </struct></baseType></dataType>
  <dataType name='E_State'><baseType><enum><values><value name='Idle' value='0'/><value name='Run'/></values><baseType><INT/></baseType></enum></baseType></dataType>
  <dataType name='T_Buffer'><baseType><array><dimension lower='0' upper='C_LEN'/><baseType><WORD/></baseType></array></baseType></dataType>
  <dataType name='P_Motor'><baseType><pointer><baseType><derived name='ST_Motor'/></baseType></pointer></baseType></dataType>
  <dataType name='T_Pct'><baseType><subrangeUnsigned><range lower='0' upper='100'/><baseType><UINT/></baseType></subrangeUnsigned></baseType></dataType>
  <dataType name='T_Name'><baseType><string length='32'/></baseType></dataType>
  <dataType name='T_Motor2'><baseType><derived name='ST_Motor'/></baseType></dataType>
</dataTypes><pous/>";

        var project = await ParseAsync(types);

        project.Programs.Should().BeEmpty();
        project.DataTypes.Select(d => (d.Name, d.Kind, d.BaseType)).Should().Equal(
            ("ST_Motor", "Struct", (string?)null),
            ("E_State", "Enum", "INT"),
            ("T_Buffer", "Alias", "ARRAY[0..C_LEN] OF WORD"),
            ("P_Motor", "Alias", "POINTER TO ST_Motor"),
            ("T_Pct", "Alias", "UINT(0..100)"),
            ("T_Name", "Alias", "STRING(32)"),
            ("T_Motor2", "Alias", "ST_Motor"));

        var motor = project.DataTypes[0];
        motor.Members.Select(m => (m.Name, m.DataType, m.Dimensions, m.InitialValue)).Should().Equal(
            ("speed", "REAL", (string?)null, "1.5"),
            ("buf", "ARRAY[1..N] OF BYTE", "1..N", (string?)null));
        project.DataTypes[1].Members.Select(m => (m.Name, m.InitialValue)).Should().Equal(("Idle", "0"), ("Run", (string?)null));
        project.DataTypes[2].Properties.Should().Contain("Construct", "Array").And.Contain("Dimensions", "0..C_LEN");
        project.DataTypes[3].Properties.Should().Contain("Construct", "Pointer");
        project.DataTypes[4].Properties.Should().Contain("Construct", "Subrange");
    }

    // ---------------------------------------------------------------- durations and tasks

    [Theory]
    [InlineData("T#10ms", 10)]
    [InlineData("t#50ms", 50)]
    [InlineData("TIME#1s500ms", 1500)]
    [InlineData("T#1s500ms", 1500)]
    [InlineData("T#100us", 0.1)]
    [InlineData("LTIME#1h2m3s4ms", 3_723_004)]
    [InlineData("T#1.5s", 1500)]
    [InlineData("T#1d", 86_400_000)]
    [InlineData("20ms", 20)]
    [InlineData("PT0.01S", 10)]
    [InlineData("PT10S", 10_000)]
    [InlineData("PT0S", 0)]
    public void ParseDurationMs_AcceptsIecAndIso8601Durations(string text, double expected)
    {
        PlcOpenText.ParseDurationMs(text).Should().BeApproximately(expected, 1e-9);
    }

    [Theory]
    [InlineData("gTaskInterval")]
    [InlineData("100")]
    [InlineData("T#")]
    [InlineData("")]
    public void ParseDurationMs_NonDurations_ReturnNull(string text)
    {
        PlcOpenText.ParseDurationMs(text).Should().BeNull();
    }

    [Fact]
    public void ParseDurationMs_IsCultureInvariant()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            PlcOpenText.ParseDurationMs("T#2.5ms").Should().Be(2.5);
            PlcOpenText.ParseDurationMs("PT0.05S").Should().Be(50);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task Tasks_LinkProgramsThroughPouInstance()
    {
        var instances = $@"<configurations><configuration name='Cfg'><resource name='Res'>
  <task name='Fast' priority='2' interval='T#1s500ms'><pouInstance name='inst' typeName='Main'/></task>
  <task name='Slow' priority='5' interval='PT0.1S'><pouInstance name='Other' typeName=''/></task>
  <task name='OnStart' priority='1' single='bStart'/>
  <task name='Settings' priority='3'><addData><data name='{Cds}tasksettings' handleUnknown='implementation'>
    <TaskSettings KindOfTask='Cyclic' Interval='t#20ms' IntervalUnit='ms'><Watchdog Enabled='true' Time='100' TimeUnit='ms'/></TaskSettings></data></addData></task>
</resource></configuration></configurations>";
        var pous = "<dataTypes/><pous>" +
                   "<pou name='Main' pouType='program'><body><ST><xhtml xmlns='http://www.w3.org/1999/xhtml'/></ST></body></pou>" +
                   "<pou name='Other' pouType='program'><body><ST><xhtml xmlns='http://www.w3.org/1999/xhtml'/></ST></body></pou></pous>";

        var project = await ParseAsync(pous, instances);

        project.Tasks.Select(t => (t.Name, t.Type, t.Priority, t.Rate)).Should().Equal(
            ("Fast", "Periodic", 2, (double?)1500),
            ("Slow", "Periodic", 5, (double?)100),
            ("OnStart", "Event", 1, (double?)null),
            ("Settings", "Periodic", 3, (double?)20));
        project.Tasks[0].ProgramNames.Should().Equal("Main");
        project.Tasks[0].Properties.Should().Contain("Instances", "inst: Main");
        project.Tasks[1].ProgramNames.Should().Equal("Other");
        project.Tasks[2].Properties.Should().Contain("Single", "bStart");
        project.Tasks[3].WatchdogTimeout.Should().Be(100);
        project.Programs.Select(p => (p.Name, p.TaskName)).Should().Equal(("Main", "Fast"), ("Other", "Slow"));
        project.Metadata.Should().NotContainKey("Warnings");
    }

    [Fact]
    public async Task Tasks_NoTasksInFile_NoTaskIsInvented()
    {
        var project = await ParseAsync(Pou("PLC_PRG", "<ST><xhtml xmlns='http://www.w3.org/1999/xhtml'>x := 1;</xhtml></ST>"));

        project.Tasks.Should().BeEmpty();
    }

    // ---------------------------------------------------------------- textual bodies

    [Fact]
    public async Task Bodies_StAndIlSourceIsAllTextOfTheXhtmlWrapper()
    {
        var pous = "<dataTypes/><pous>" +
                   "<pou name='A' pouType='function'><interface><returnType><INT/></returnType></interface><body><ST>\n  <xhtml xmlns='http://www.w3.org/1999/xhtml'>IF a &lt; b THEN\r\n\tA := 1;\r\nEND_IF</xhtml>\n</ST></body></pou>" +
                   "<pou name='B' pouType='functionBlock'><body><IL><xhtml:p><![CDATA[LD x\nST y]]></xhtml:p></IL></body></pou>" +
                   "<pou name='C' pouType='program'><body><ST><xhtml:p>a := 1;</xhtml:p><xhtml:p>b := 2;</xhtml:p></ST></body></pou>" +
                   "</pous>";

        var project = await ParseAsync(pous);

        project.Programs.Select(p => (p.Name, p.Type, p.Language)).Should().Equal(
            ("A", "Function", "ST"), ("B", "FunctionBlock", "IL"), ("C", "Program", "ST"));
        project.Programs[0].Routines.Single().Source.Should().Be("IF a < b THEN\n\tA := 1;\nEND_IF");
        project.Programs[0].Properties.Should().Contain("ReturnType", "INT");
        project.Programs[1].Routines.Single().Source.Should().Be("LD x\nST y");
        project.Programs[2].Routines.Single().Source.Should().Be("a := 1;\nb := 2;");
    }

    // ---------------------------------------------------------------- graphical bodies

    private static string Contact(int id, string variable, string inputs, string attrs = "") =>
        $"<contact localId='{id}' {attrs}><position x='0' y='0'/><connectionPointIn>{inputs}</connectionPointIn><connectionPointOut/><variable>{variable}</variable></contact>";

    private static string Coil(int id, string variable, string inputs, string attrs = "") =>
        $"<coil localId='{id}' {attrs}><position x='0' y='0'/><connectionPointIn>{inputs}</connectionPointIn><connectionPointOut/><variable>{variable}</variable></coil>";

    private static string From(params int[] ids) => string.Concat(ids.Select(i => $"<connection refLocalId='{i}'/>"));

    private static string Rail(int id) =>
        $"<leftPowerRail localId='{id}'><position x='0' y='0'/><connectionPointOut formalParameter=''/></leftPowerRail>";

    [Fact]
    public async Task Ladder_ParallelBranchesFactorCommonPrefix()
    {
        var ld = "<LD>" + Rail(1) +
                 Contact(2, "a", From(1)) +
                 Contact(3, "b", From(2)) +
                 Contact(4, "c", From(2), "negated='true'") +
                 Coil(5, "d", From(3, 4)) +
                 Coil(6, "e", From(2), "storage='reset'") +
                 "<rightPowerRail localId='7'><position x='0' y='0'/><connectionPointIn>" + From(5, 6) + "</connectionPointIn></rightPowerRail></LD>";

        var project = await ParseAsync(Pou("Main", ld));

        var routine = project.Programs.Single().Routines.Single();
        routine.Language.Should().Be("LD");
        var network = routine.Networks.Should().ContainSingle().Subject;
        network.Text.Should().Be("XIC(a) [XIC(b) , XIO(c)] OTE(d)\nXIC(a) OTU(e)");
        network.Instructions.Select(i => i.Mnemonic).Should().Equal("XIC", "XIC", "XIO", "OTE", "OTU");
        network.Instructions.Select(i => i.Position).Should().Equal(0, 1, 2, 3, 4);
    }

    [Fact]
    public async Task Ladder_RungsSharingOnePowerRail_AreSeparateNetworksInPositionOrder()
    {
        var ld = "<LD>" + Rail(1) +
                 "<contact localId='20'><position x='10' y='200'/><connectionPointIn>" + From(1) + "</connectionPointIn><variable>second</variable></contact>" +
                 "<coil localId='21'><position x='90' y='200'/><connectionPointIn>" + From(20) + "</connectionPointIn><variable>out2</variable></coil>" +
                 "<comment localId='9'><position x='0' y='0'/><content><xhtml:p>First rung</xhtml:p></content></comment>" +
                 "<contact localId='10' edge='falling'><position x='10' y='50'/><connectionPointIn>" + From(1) + "</connectionPointIn><variable>first</variable></contact>" +
                 "<coil localId='11' negated='true'><position x='90' y='50'/><connectionPointIn>" + From(10) + "</connectionPointIn><variable>out1</variable></coil>" +
                 "</LD>";

        var project = await ParseAsync(Pou("Main", ld));

        var networks = project.Programs.Single().Routines.Single().Networks;
        networks.Select(n => (n.Number, n.Text, n.Comment)).Should().Equal(
            (1, "XICN(first) OTN(out1)", "First rung"),
            (2, "XIC(second) OTE(out2)", (string?)null));
    }

    [Fact]
    public async Task Fbd_FunctionBlockInstanceGetsItsOwnLineAndOutputsAreReferenced()
    {
        var fbd = @"<FBD>
  <inVariable localId='1'><position x='0' y='0'/><connectionPointOut/><expression>start</expression></inVariable>
  <inVariable localId='2'><position x='0' y='20'/><connectionPointOut/><expression>T#2s</expression></inVariable>
  <block localId='3' typeName='TON' instanceName='t1'><position x='50' y='0'/>
    <inputVariables>
      <variable formalParameter='IN'><connectionPointIn><connection refLocalId='1'/></connectionPointIn></variable>
      <variable formalParameter='PT'><connectionPointIn><connection refLocalId='2'/></connectionPointIn></variable>
    </inputVariables><inOutVariables/>
    <outputVariables><variable formalParameter='Q'><connectionPointOut/></variable><variable formalParameter='ET'><connectionPointOut/></variable></outputVariables>
  </block>
  <outVariable localId='4'><position x='100' y='0'/><connectionPointIn><connection refLocalId='3' formalParameter='Q'/></connectionPointIn><expression>done</expression></outVariable>
  <outVariable localId='5' negated='true'><position x='100' y='20'/><connectionPointIn><connection refLocalId='3' formalParameter='ET'/></connectionPointIn><expression>elapsed</expression></outVariable>
</FBD>";

        var project = await ParseAsync(Pou("Main", fbd));

        var network = project.Programs.Single().Routines.Single().Networks.Single();
        network.Language.Should().Be("FBD");
        network.Text.Should().Be("t1(IN := start, PT := T#2s)\ndone := t1.Q\nNOT(elapsed) := t1.ET");
        network.Instructions.Single().Should().BeEquivalentTo(new { Mnemonic = "TON", Type = "FunctionBlock" });
        network.Instructions.Single().Operands.Should().Equal("t1", "start", "T#2s");
    }

    [Fact]
    public async Task Fbd_CodesysOutputPinExpressionAndCfcJunctionConnectors()
    {
        // CODESYS V3.5 SP18 writes the assignment on the output pin and uses unnamed connectors in CFC.
        var body = $@"<ST><xhtml xmlns='http://www.w3.org/1999/xhtml'/></ST>
<addData><data name='{Cds}cfc' handleUnknown='implementation'><CFC>
  <inVariable localId='4'><position x='2' y='3'/><connectionPointOut><expression/></connectionPointOut><expression>a</expression></inVariable>
  <connector localId='3' name=''><position x='0' y='0'/><connectionPointIn><connection refLocalId='4' formalParameter='a'/></connectionPointIn></connector>
  <block localId='2' typeName='ABS'><position x='7' y='2'/>
    <inputVariables><variable formalParameter='In1'><connectionPointIn><connection refLocalId='3'/></connectionPointIn></variable></inputVariables>
    <inOutVariables/><outputVariables><variable formalParameter='Out1'><connectionPointOut><expression>result</expression></connectionPointOut></variable></outputVariables>
  </block>
</CFC></data></addData>";

        var project = await ParseAsync(Pou("Main", body));

        var routine = project.Programs.Single().Routines.Single();
        routine.Language.Should().Be("CFC");
        routine.Networks.Single().Text.Should().Be("result := ABS(In1 := a)");
        routine.Networks.Single().Instructions.Single().Operands.Should().Equal("a", "result");
    }

    [Fact]
    public async Task Fbd_FeedbackLoopAndDanglingReference_DoNotHangAndAreReported()
    {
        var fbd = @"<FBD>
  <block localId='1' typeName='ADD'><position x='0' y='0'/>
    <inputVariables>
      <variable formalParameter='IN1'><connectionPointIn><connection refLocalId='2'/></connectionPointIn></variable>
      <variable formalParameter='IN2'><connectionPointIn><connection refLocalId='99'/></connectionPointIn></variable>
    </inputVariables><inOutVariables/><outputVariables><variable formalParameter='OUT'><connectionPointOut/></variable></outputVariables>
  </block>
  <block localId='2' typeName='MUL'><position x='50' y='0'/>
    <inputVariables><variable formalParameter='IN1'><connectionPointIn><connection refLocalId='1'/></connectionPointIn></variable></inputVariables>
    <inOutVariables/><outputVariables><variable formalParameter='OUT'><connectionPointOut/></variable></outputVariables>
  </block>
  <outVariable localId='3'><position x='100' y='0'/><connectionPointIn><connection refLocalId='2'/></connectionPointIn><expression>y</expression></outVariable>
</FBD>";

        var project = await ParseAsync(Pou("Main", fbd));

        var network = project.Programs.Single().Routines.Single().Networks.Single();
        // MUL feeds both ADD and y, so it gets its own line and ADD refers back to it by name.
        network.Text.Should().Be("MUL(IN1 := ADD(IN1 := MUL@2.OUT, IN2 := ?99))\ny := MUL@2.OUT");
        project.Metadata["Warnings"].Should().Contain("element 99, which does not exist");
    }

    [Fact]
    public async Task Ladder_VeryLongSeriesChain_IsTruncatedWithWarningInsteadOfOverflowingTheStack()
    {
        var sb = new StringBuilder("<LD>").Append(Rail(1));
        const int count = 5000;
        for (var i = 2; i < count; i++)
            sb.Append(Contact(i, "x" + i, From(i - 1)));
        sb.Append(Coil(count, "q", From(count - 1))).Append("</LD>");

        var project = await ParseAsync(Pou("Main", sb.ToString()));

        project.Programs.Single().Routines.Single().Networks.Single().Text.Should().Contain("OTE(q)");
        project.Metadata["Warnings"].Should().Contain("truncated");
    }

    // ---------------------------------------------------------------- CODESYS addData

    [Fact]
    public async Task Codesys_MethodsPropertiesActionsAndInheritance_BecomeRoutines()
    {
        var extra = $@"<addData>
  <data name='{Cds}method' handleUnknown='implementation'><Method name='Start'>
    <interface><returnType><BOOL/></returnType><inputVars><variable name='speed'><type><REAL/></type></variable></inputVars>
      <addData><data name='{Cds}accessmodifiers' handleUnknown='implementation'><AccessModifiers Private='true'/></data></addData></interface>
    <body><ST><xhtml xmlns='http://www.w3.org/1999/xhtml'>Start := TRUE;</xhtml></ST></body></Method></data>
  <data name='{Cds}property' handleUnknown='implementation'><Property name='Speed'>
    <interface><returnType><LREAL/></returnType></interface>
    <SetAccessor><interface/><body><ST><xhtml xmlns='http://www.w3.org/1999/xhtml'>_speed := Speed;</xhtml></ST></body></SetAccessor>
    <GetAccessor><interface/><body><ST><xhtml xmlns='http://www.w3.org/1999/xhtml'>Speed := _speed;</xhtml></ST></body></GetAccessor></Property></data>
</addData>";
        var iface = $"<localVars>{Var("_speed", "<LREAL/>")}</localVars><addData><data name='{Cds}pouinheritance' handleUnknown='implementation'>" +
                    "<Inheritance><Extends>FB_Base</Extends><Implements>I_Motor</Implements><Implements>I_Named</Implements></Inheritance></data></addData>";
        var pou = $@"<dataTypes/><pous><pou name='FB_Motor' pouType='functionBlock'><interface>{iface}</interface>
  <actions><action name='Reset'><body><ST><xhtml xmlns='http://www.w3.org/1999/xhtml'>_speed := 0;</xhtml></ST></body></action></actions>
  <body><ST><xhtml xmlns='http://www.w3.org/1999/xhtml'/></ST></body>{extra}</pou></pous>";

        var project = await ParseAsync(pou);

        var fb = project.Programs.Single();
        fb.Type.Should().Be("FunctionBlock");
        fb.Properties.Should().Contain("Extends", "FB_Base").And.Contain("Implements", "I_Motor, I_Named");
        fb.Routines.Select(r => (r.Name, r.Type, r.Source)).Should().Equal(
            ("FB_Motor", "Main", ""),
            ("Reset", "Action", "_speed := 0;"),
            ("Start", "Method", "Start := TRUE;"),
            ("Speed.Get", "Property", "Speed := _speed;"),
            ("Speed.Set", "Property", "_speed := Speed;"));
        var start = fb.Routines.Single(r => r.Name == "Start");
        start.Properties.Should().Contain("ReturnType", "BOOL").And.Contain("AccessModifier", "PRIVATE")
            .And.Contain("Parameters", "VAR_INPUT speed : REAL");
        fb.Routines.Single(r => r.Name == "Speed.Get").Properties.Should().Contain("DataType", "LREAL");
    }

    [Fact]
    public async Task Codesys_ProjectAddData_GvlInterfaceUnionAndResourcePous()
    {
        var addData = $@"
<data name='{Cds}globalvars' handleUnknown='implementation'><globalVars name='GVL_Const' constant='true'>{Var("MAX", "<INT/>")}</globalVars></data>
<data name='{Cds}interface' handleUnknown='implementation'><Interface name='I_Motor'>
  <Methods><Method name='Start'><interface><returnType><BOOL/></returnType></interface><addData/></Method></Methods>
  <Properties><Property name='Speed'><interface><returnType><REAL/></returnType></interface><GetAccessor/><addData/></Property></Properties>
  <Inheritance><Extends>I_Base</Extends></Inheritance></Interface></data>
<data name='{Cds}union' handleUnknown='implementation'><union name='U_Word'>{Var("w", "<WORD/>")}{Var("b", "<array><dimension lower='0' upper='1'/><baseType><BYTE/></baseType></array>")}</union></data>";
        var instances = $@"<configurations><configuration name='Device'><resource name='Application'>
  <task name='MainTask' priority='1' interval='PT0.02S'><pouInstance name='PLC_PRG' typeName=''/></task>
  <globalVars name='GVL'>{Var("counter", "<DINT/>")}</globalVars>
  <addData>
    <data name='{Cds}pou' handleUnknown='implementation'><pou name='PLC_PRG' pouType='program'><interface/><body><ST><xhtml xmlns='http://www.w3.org/1999/xhtml'>counter := counter + 1;</xhtml></ST></body></pou></data>
    <data name='{Cds}datatype' handleUnknown='implementation'><dataType name='ST_X'><baseType><struct>{Var("v", "<BOOL/>")}</struct></baseType></dataType></data>
  </addData>
</resource></configuration></configurations>";

        var project = await ParseAsync("<dataTypes/><pous/>", instances, addData);

        project.Tags.Select(t => (t.Name, t.Scope)).Should().Equal(("counter", "GVL"), ("MAX", "GVL_Const"));
        project.Tags[1].Properties.Should().Contain("Constant", "true");
        project.Programs.Select(p => (p.Name, p.Type, p.TaskName)).Should().Equal(
            ("PLC_PRG", "Program", "MainTask"), ("I_Motor", "Interface", (string?)null));
        project.Programs[0].Properties.Should().Contain("Resource", "Application");
        var itf = project.Programs[1];
        itf.Properties.Should().Contain("Extends", "I_Base");
        itf.Routines.Select(r => (r.Name, r.Type, r.Source)).Should().Equal(("Start", "Method", (string?)null), ("Speed", "Property", (string?)null));
        project.DataTypes.Select(d => (d.Name, d.Kind)).Should().Equal(("ST_X", "Struct"), ("U_Word", "Union"));
        project.DataTypes[1].Members.Select(m => m.DataType).Should().Equal("WORD", "ARRAY[0..1] OF BYTE");
    }

    [Fact]
    public async Task Codesys_MixedAttributeGvl_IsNotDoubleCountedAndKeepsPerVariableQualifiers()
    {
        // Shape seen in CODESYS V3.5 SP21: the flattened list plus addData "mixedattrsvarlist" with the real sections.
        var instances = $@"<configurations><configuration name='Device'><resource name='App'>
  <globalVars name='GVL' constant='true'>{Var("LEN", "<INT/>")}{Var("state", "<INT/>")}
    <addData><data name='{Cds}mixedattrsvarlist' handleUnknown='implementation'><MixedAttrsVarList>
      <globalVars name='GVL' constant='true'>{Var("LEN", "<INT/>")}</globalVars>
      <globalVars name='GVL'>{Var("state", "<INT/>")}</globalVars>
    </MixedAttrsVarList></data></addData>
  </globalVars>
</resource></configuration></configurations>";

        var project = await ParseAsync("<dataTypes/><pous/>", instances);

        project.Tags.Select(t => t.Name).Should().Equal("LEN", "state");
        project.Tags[0].Properties.Should().Contain("Constant", "true");
        project.Tags[1].Properties.Should().NotContainKey("Constant");
    }

    [Fact]
    public async Task Codesys_DeviceTree_ModulesAndChannelsAreCountedOnce()
    {
        static string Device(string ident, string parameters) =>
            $"<data name='Device' handleUnknown='discard'><Device xmlns=''><DeviceType>{ident}<Connector><HostParameterSet>{parameters}</HostParameterSet></Connector></DeviceType></Device></data>";
        const string inputs = "<Parameter ParameterId='1'><Attributes channel='input'/><Value><Element name='BIT0'/><Element name='BIT1'/></Value>" +
                              "<Mapping><Element name='BIT0'>DI_1</Element></Mapping></Parameter>";
        const string outputs = "<Parameter ParameterId='2'><Attributes channel='output'/><Value/></Parameter>";
        var instances = $@"<configurations><configuration name='PLC'>
  <addData>{Device("<DeviceIdentification><Type>4096</Type><Id>0001</Id><Version>4.1.0.0</Version></DeviceIdentification>", "")}
    <data name='configurations' handleUnknown='discard'><configurations><configuration name='Bus'>
      <addData>{Device("<DeviceIdentification><Type>288</Type><Id>0002</Id><Version>1.0</Version></DeviceIdentification>", "")}
        <data name='configurations' handleUnknown='discard'><configurations>
          <configuration name='DI'><addData>{Device("<ModuleIdentification><Type>1</Type><Id>2</Id><Version>1.0</Version><ModuleId>750-430</ModuleId></ModuleIdentification>", inputs)}</addData></configuration>
          <configuration name='DO'><addData>{Device("<ModuleIdentification><Type>1</Type><Id>3</Id><Version>1.0</Version><ModuleId>750-530</ModuleId></ModuleIdentification>", outputs)}</addData></configuration>
        </configurations></data>
      </addData>
    </configuration></configurations></data>
  </addData>
</configuration></configurations>";

        var project = await ParseAsync("<dataTypes/><pous/>", instances);

        project.Controller!.Type.Should().Be("PLC");
        project.Controller.FirmwareRevision.Should().Be("4.1.0.0");
        project.IoModules.Select(m => (m.Name, m.Type, m.CatalogNumber, m.ParentModule, m.ChannelCount)).Should().Equal(
            ("Bus", "Device", "288/0002", "PLC", 0),
            ("DI", "Input", "750-430", "Bus", 2),
            ("DO", "Output", "750-530", "Bus", 1));
        project.IoModules[1].AssociatedTags.Should().Equal("DI_1");
    }
}
