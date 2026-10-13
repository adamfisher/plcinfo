using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using PlcInfo.Parsing;
using PlcInfo.Parsing.SimaticMl;
using Xunit.Categories;

namespace PlcInfo.Tests.Parsing;

[UnitTest]
public class SiemensTiaPortalParserTests : IDisposable
{
    private const string FlgNs = "http://www.siemens.com/automation/Openness/SW/NetworkSource/FlgNet/v5";

    private readonly SiemensTiaPortalParser _parser = new();
    private readonly string _dir;

    public SiemensTiaPortalParserTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tia_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    // ---- detection -----------------------------------------------------------------

    [Theory]
    [InlineData("test.zap13")]
    [InlineData("test.zap15_1")]
    [InlineData("test.ap15_1")]
    [InlineData("test.ap17")]
    [InlineData("test.AP21")]
    [InlineData("test.ZAP18")]
    public void CanParse_ProjectFilesAndArchives_AreClaimed(string fileName)
    {
        _parser.CanParse(fileName).Should().BeTrue();
    }

    [Theory]
    [InlineData("test.zip")]
    [InlineData("test.s7p")]
    [InlineData("missing.xml")]
    public void CanParse_OtherFiles_AreNotClaimed(string fileName)
    {
        _parser.CanParse(Path.Combine(_dir, fileName)).Should().BeFalse();
    }

    [Theory]
    [InlineData("<project xmlns='http://www.plcopen.org/xml/tc6_0200'><fileHeader productName='CODESYS'/></project>")]
    [InlineData("<TcSmProject/>")]
    [InlineData("<Document><Engineering version='V17'/><Something/></Document>")]
    [InlineData("<Document xmlns='urn:other'><SW.Blocks.FB/></Document>")]
    [InlineData("not xml at all")]
    public void CanParse_XmlThatIsNotSimaticMl_IsNotClaimed(string content)
    {
        var path = Write("other.xml", content);

        _parser.CanParse(path).Should().BeFalse();
    }

    [Fact]
    public void CanParse_XmlWithDtd_IsNotClaimed()
    {
        var path = Write("dtd.xml", "<!DOCTYPE Document [<!ENTITY x 'y'>]><Document><SW.Blocks.FB/></Document>");

        _parser.CanParse(path).Should().BeFalse();
    }

    [Fact]
    public void CanParse_SimaticMlFileAndFolder_AreClaimed()
    {
        var path = Write(Path.Combine("Program blocks", "fb.xml"), Block("FB", "LAD", string.Empty));

        _parser.CanParse(path).Should().BeTrue();
        _parser.CanParse(_dir).Should().BeTrue();
        _parser.CanParse(Path.Combine(_dir, "Program blocks")).Should().BeTrue();
    }

    [Fact]
    public void CanParse_FolderWithoutExports_IsNotClaimed()
    {
        Write("readme.xml", "<root/>");

        _parser.CanParse(_dir).Should().BeFalse();
    }

    // ---- .apXX / .zapXX ------------------------------------------------------------

    [Theory]
    [InlineData("project.ap17", "TIA Portal V17 project file")]
    [InlineData("project.zap15_1", "TIA Portal V15.1 project archive")]
    public async Task ParseAsync_ProjectFile_ThrowsNotSupportedWithExportAdvice(string fileName, string expected)
    {
        var path = Path.Combine(_dir, fileName);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            archive.CreateEntry("project.ap17");

        var action = async () => await _parser.ParseAsync(path);

        var ex = (await action.Should().ThrowAsync<NotSupportedException>()).Which;
        ex.Message.Should().Contain(expected).And.Contain("SimaticML").And.Contain("Version Control Interface");
    }

    [Fact]
    public async Task ParseAsync_MissingXml_ThrowsFileNotFound()
    {
        var path = Path.Combine(_dir, "missing.xml");

        var action = async () => await _parser.ParseAsync(path);

        await action.Should().ThrowAsync<FileNotFoundException>().WithMessage($"File not found: {path}");
    }

    [Fact]
    public async Task ParseAsync_XmlThatIsNotSimaticMl_Throws()
    {
        var path = Write("other.xml", "<root/>");

        var action = async () => await _parser.ParseAsync(path);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not a TIA Portal Openness (SimaticML) export*");
    }

    [Fact]
    public async Task ParseAsync_FolderWithoutExports_Throws()
    {
        Write("readme.xml", "<root/>");

        var action = async () => await _parser.ParseAsync(_dir);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("No TIA Portal Openness (SimaticML) exports were found*");
    }

    // ---- blocks --------------------------------------------------------------------

    [Fact]
    public async Task ParseAsync_Block_ReadsAttributeListInterfaceAndCultures()
    {
        // Interface namespace versions vary by TIA release; any /vN must be accepted.
        var xml = @"<Document><Engineering version='V19'/>
<SW.Blocks.FB ID='0'><AttributeList>
  <Interface><Sections xmlns='http://www.siemens.com/automation/Openness/SW/Interface/v99'>
    <Section Name='Input'><Member Name='Start' Datatype='Bool'><Comment><MultiLanguageText Lang='de-DE'>Start DE</MultiLanguageText><MultiLanguageText Lang='en-US'>Start EN</MultiLanguageText></Comment></Member></Section>
    <Section Name='Static'><Member Name='Buffer' Datatype='Array[1..3, 0..1] of &quot;udt_X&quot;'/></Section>
  </Sections></Interface>
  <Name>Motor</Name><Number>12</Number><ProgrammingLanguage>LAD</ProgrammingLanguage><HeaderVersion>0.1</HeaderVersion>
</AttributeList><ObjectList>
  <MultilingualText ID='1' CompositionName='Title'><ObjectList>
    <MultilingualTextItem ID='2' CompositionName='Items'><AttributeList><Culture>de-DE</Culture><Text>Motor DE</Text></AttributeList></MultilingualTextItem>
    <MultilingualTextItem ID='3' CompositionName='Items'><AttributeList><Culture>en-US</Culture><Text>Motor EN</Text></AttributeList></MultilingualTextItem>
  </ObjectList></MultilingualText>
  <MultilingualText ID='4' CompositionName='Comment'><ObjectList>
    <MultilingualTextItem ID='5' CompositionName='Items'><AttributeList><Culture>en-US</Culture><Text /></AttributeList></MultilingualTextItem>
    <MultilingualTextItem ID='6' CompositionName='Items'><AttributeList><Culture>fr-FR</Culture><Text>Moteur</Text></AttributeList></MultilingualTextItem>
  </ObjectList></MultilingualText>
</ObjectList></SW.Blocks.FB></Document>";

        var project = await _parser.ParseAsync(Write("motor.xml", xml));

        var block = project.Blocks.Should().ContainSingle().Subject;
        (block.Name, block.Number, block.Type, block.Language).Should().Be(("Motor", 12, "FB", "LAD"));
        block.Title.Should().Be("Motor EN");
        block.Comment.Should().Be("Moteur");
        block.Properties["HeaderVersion"].Should().Be("0.1");
        block.Interface.Should().Equal(new Dictionary<string, string>
        {
            ["Input.Start"] = "Bool",
            ["Static.Buffer"] = "Array[1..3, 0..1] of \"udt_X\""
        });
        project.Metadata["TiaVersion"].Should().Be("V19");
    }

    [Fact]
    public async Task ParseAsync_GlobalDb_MembersAreDbScopedTags()
    {
        var xml = @"<Document><Engineering version='V18'/><SW.Blocks.GlobalDB ID='0'><AttributeList>
<Interface><Sections xmlns='http://www.siemens.com/automation/Openness/SW/Interface/v5'><Section Name='Static'>
  <Member Name='Speed' Datatype='Real' Remanence='Retain'><Comment><MultiLanguageText Lang='en-US'>Set speed</MultiLanguageText></Comment><StartValue>1.5</StartValue></Member>
  <Member Name='Axis' Datatype='Struct'><Member Name='Pos' Datatype='Array[0..9] of DInt'/></Member>
</Section></Sections></Interface>
<Name>Data</Name><Number>3</Number><ProgrammingLanguage>DB</ProgrammingLanguage></AttributeList></SW.Blocks.GlobalDB></Document>";

        var project = await _parser.ParseAsync(Write("db.xml", xml));

        project.Blocks.Single().Properties["DbType"].Should().Be("Global");
        project.Tags.Select(t => (t.Name, t.DataType, t.Scope, t.InitialValue)).Should().Equal(
            ("Speed", "Real", "Data", "1.5"), ("Axis", "Struct", "Data", null), ("Axis.Pos", "DInt", "Data", null));
        project.Tags[0].Description.Should().Be("Set speed");
        project.Tags[0].Properties["Remanence"].Should().Be("Retain");
        project.Tags[2].ArrayDimensions.Should().Equal(10);
    }

    [Fact]
    public async Task ParseAsync_TagTable_AddressesIoFlagsAndConstants()
    {
        var xml = @"<Document><Engineering version='V20'/><SW.Tags.PlcTagTable ID='0'><AttributeList><Name>IO</Name></AttributeList><ObjectList>
  <SW.Tags.PlcTag ID='1' CompositionName='Tags'><AttributeList><DataTypeName>Bool</DataTypeName><LogicalAddress>%I0.0</LogicalAddress><Name>Start</Name></AttributeList></SW.Tags.PlcTag>
  <SW.Tags.PlcTag ID='2' CompositionName='Tags'><AttributeList><DataTypeName>Word</DataTypeName><LogicalAddress>%QW64</LogicalAddress><Name>Speed</Name></AttributeList></SW.Tags.PlcTag>
  <SW.Tags.PlcTag ID='3' CompositionName='Tags'><AttributeList><DataTypeName>Bool</DataTypeName><LogicalAddress>%M0.1</LogicalAddress><Name>Flag</Name></AttributeList></SW.Tags.PlcTag>
  <SW.Tags.PlcTag ID='4' CompositionName='Tags'><AttributeList><DataTypeName>&quot;udt_X&quot;</DataTypeName><Name>Unplaced</Name></AttributeList></SW.Tags.PlcTag>
  <SW.Tags.PlcUserConstant ID='5' CompositionName='UserConstants'><AttributeList><DataTypeName>Int</DataTypeName><Name>MAX</Name><Value>10</Value></AttributeList></SW.Tags.PlcUserConstant>
</ObjectList></SW.Tags.PlcTagTable></Document>";

        var project = await _parser.ParseAsync(Write("io.xml", xml));

        project.Tags.Select(t => (t.Name, t.DataType, t.Address, t.IsIoTag)).Should().Equal(
            ("Start", "Bool", "%I0.0", true), ("Speed", "Word", "%QW64", true), ("Flag", "Bool", "%M0.1", false),
            ("Unplaced", "udt_X", (string?)null, false), ("MAX", "Int", (string?)null, false));
        project.Tags[4].Usage.Should().Be("Constant");
        project.Tags[4].InitialValue.Should().Be("10");
    }

    [Fact]
    public async Task ParseAsync_Folder_SkipsBrokenAndUnsupportedFilesWithWarnings()
    {
        Write(Path.Combine("blocks", "a.xml"), Block("FC", "LAD", string.Empty, name: "Good"));
        Write(Path.Combine("blocks", "b.xml"), "<Document><SW.Blocks.FC><AttributeList><Name>Broken</Name>");
        Write(Path.Combine("watch", "w.xml"), "<Document><Engineering version='V18'/><SW.WatchAndForceTables.PlcWatchTable ID='0'/></Document>");
        Write(Path.Combine("blocks", "kh.xml"), Block("FB", "SCL", string.Empty, name: "Secret", extra: "<IsKnowHowProtected>true</IsKnowHowProtected>"));

        var project = await _parser.ParseAsync(_dir);

        project.Blocks.Select(b => b.Name).Should().Equal("Good", "Secret");
        var warnings = project.Metadata["Warnings"].Split('\n');
        warnings.Should().HaveCount(3);
        warnings.Should().Contain(w => w.StartsWith("blocks/b.xml: The file is not well-formed XML and was skipped"));
        warnings.Should().Contain("watch/w.xml: SW.WatchAndForceTables.PlcWatchTable exports are not supported and were skipped.");
        warnings.Should().Contain("blocks/kh.xml: Block Secret is know-how protected, so its logic is not in the export.");
    }

    // ---- LAD / FBD -----------------------------------------------------------------

    [Fact]
    public void FlgNet_SeriesParallelAndNegation()
    {
        // Powerrail ─┬─[Start]─┬─[/Stop]──(Run)
        //            └─[Run]───┘
        var result = RenderLad(
            Parts(Access(1, "LocalVariable", "Start"), Access(2, "LocalVariable", "Run"), Access(3, "LocalVariable", "Stop"),
                Access(4, "LocalVariable", "Run"),
                "<Part Name='Contact' UId='10'/><Part Name='Contact' UId='11'/>" +
                "<Part Name='O' UId='12'><TemplateValue Name='Card' Type='Cardinality'>2</TemplateValue></Part>" +
                "<Part Name='Contact' UId='13'><Negated Name='operand'/></Part><Part Name='Coil' UId='14'/>"),
            Wire("<Powerrail/>", "<NameCon UId='10' Name='in'/>", "<NameCon UId='11' Name='in'/>"),
            Wire("<IdentCon UId='1'/>", "<NameCon UId='10' Name='operand'/>"),
            Wire("<IdentCon UId='2'/>", "<NameCon UId='11' Name='operand'/>"),
            Wire("<NameCon UId='10' Name='out'/>", "<NameCon UId='12' Name='in1'/>"),
            Wire("<NameCon UId='11' Name='out'/>", "<NameCon UId='12' Name='in2'/>"),
            Wire("<NameCon UId='12' Name='out'/>", "<NameCon UId='13' Name='in'/>"),
            Wire("<IdentCon UId='3'/>", "<NameCon UId='13' Name='operand'/>"),
            Wire("<NameCon UId='13' Name='out'/>", "<NameCon UId='14' Name='in'/>"),
            Wire("<IdentCon UId='4'/>", "<NameCon UId='14' Name='operand'/>"));

        result.Text.Should().Be("Coil(#Run) := (#Start OR #Run) AND NOT #Stop");
        result.Instructions.Select(i => i.Mnemonic).Should().Equal("Contact", "Contact", "O", "Contact", "Coil");
        result.Instructions[4].Operands.Should().Equal("#Run");
    }

    [Fact]
    public void FlgNet_IsIndependentOfUidNumbering()
    {
        string Net(int offset) => RenderLad(
            Parts(Access(1 + offset, "GlobalVariable", "A"), Access(2 + offset, "GlobalVariable", "Q"),
                $"<Part Name='Contact' UId='{10 + offset}'/><Part Name='Coil' UId='{11 + offset}'/>"),
            Wire("<Powerrail/>", $"<NameCon UId='{10 + offset}' Name='in'/>"),
            Wire($"<IdentCon UId='{1 + offset}'/>", $"<NameCon UId='{10 + offset}' Name='operand'/>"),
            Wire($"<NameCon UId='{10 + offset}' Name='out'/>", $"<NameCon UId='{11 + offset}' Name='in'/>"),
            Wire($"<IdentCon UId='{2 + offset}'/>", $"<NameCon UId='{11 + offset}' Name='operand'/>")).Text;

        Net(0).Should().Be("Coil(\"Q\") := \"A\"").And.Be(Net(100));
    }

    [Fact]
    public void FlgNet_BoxWithEnableOutputsAndAbsoluteAddress()
    {
        var result = RenderLad(
            Parts("<Access Scope='Address' UId='1'><Address Area='Input' Type='Bool' BitOffset='10'/></Access>",
                "<Access Scope='LiteralConstant' UId='2'><Constant><ConstantType>Int</ConstantType><ConstantValue>5</ConstantValue></Constant></Access>",
                "<Access Scope='GlobalVariable' UId='3'><Symbol><Component Name='Data'/><Component Name='arr' AccessModifier='Array'><Access Scope='LiteralConstant'><Constant><ConstantValue>2</ConstantValue></Constant></Access></Component></Symbol></Access>",
                "<Part Name='Contact' UId='10'/><Part Name='Move' UId='11' DisabledENO='true'><TemplateValue Name='Card' Type='Cardinality'>1</TemplateValue></Part>"),
            Wire("<Powerrail/>", "<NameCon UId='10' Name='in'/>"),
            Wire("<IdentCon UId='1'/>", "<NameCon UId='10' Name='operand'/>"),
            Wire("<NameCon UId='10' Name='out'/>", "<NameCon UId='11' Name='en'/>"),
            Wire("<IdentCon UId='2'/>", "<NameCon UId='11' Name='in'/>"),
            Wire("<NameCon UId='11' Name='out1'/>", "<IdentCon UId='3'/>"));

        result.Text.Should().Be("Move(en := %I1.2, in := 5, out1 => \"Data\".arr[2])");
        result.Instructions[1].Operands.Should().Equal("5", "\"Data\".arr[2]");
    }

    [Fact]
    public void FlgNet_FanOutBeforeParallelBranchIsFactoredOut()
    {
        var result = RenderLad(
            Parts(Access(1, "LocalVariable", "a"), Access(2, "LocalVariable", "b"), Access(3, "LocalVariable", "c"),
                Access(4, "LocalVariable", "q"),
                "<Part Name='Contact' UId='10'/><Part Name='Contact' UId='11'/><Part Name='Contact' UId='12'/>" +
                "<Part Name='O' UId='13'/><Part Name='Coil' UId='14'/>"),
            Wire("<Powerrail/>", "<NameCon UId='10' Name='in'/>"),
            Wire("<IdentCon UId='1'/>", "<NameCon UId='10' Name='operand'/>"),
            Wire("<NameCon UId='10' Name='out'/>", "<NameCon UId='11' Name='in'/>", "<NameCon UId='12' Name='in'/>"),
            Wire("<IdentCon UId='2'/>", "<NameCon UId='11' Name='operand'/>"),
            Wire("<IdentCon UId='3'/>", "<NameCon UId='12' Name='operand'/>"),
            Wire("<NameCon UId='11' Name='out'/>", "<NameCon UId='13' Name='in1'/>"),
            Wire("<NameCon UId='12' Name='out'/>", "<NameCon UId='13' Name='in2'/>"),
            Wire("<NameCon UId='13' Name='out'/>", "<NameCon UId='14' Name='in'/>"),
            Wire("<IdentCon UId='4'/>", "<NameCon UId='14' Name='operand'/>"));

        result.Text.Should().Be("Coil(#q) := #a AND (#b OR #c)");
    }

    // ---- SCL / STL -----------------------------------------------------------------

    [Fact]
    public void Scl_TokensCommentsAndGlobals()
    {
        var st = XElement.Parse(@"<StructuredText xmlns='http://www.siemens.com/automation/Openness/SW/NetworkSource/StructuredText/v4'>
<LineComment><Text> set output</Text></LineComment><NewLine/>
<Access Scope='GlobalVariable'><Symbol><Component Name='Out DB'/><Token Text='.'/><Component Name='x'/></Symbol></Access>
<Blank/><Token Text=':='/><Blank/>
<Access Scope='LocalVariable'><Symbol><Component Name='a'/></Symbol></Access><Blank/><Token Text='AND'/><Blank/>
<Access Scope='LiteralConstant'><Constant><ConstantValue>TRUE</ConstantValue></Constant></Access><Token Text=';'/><Blank Num='3'/>
<LineComment Inserted='true'><Text>done</Text></LineComment><NewLine Num='2'/>
</StructuredText>");

        var (text, _) = SclRenderer.Render(st);

        text.Should().Be("// set output\n\"Out DB\".x := #a AND TRUE;   (*done*)");
    }

    [Fact]
    public void Stl_TokenNamesAreMappedToMnemonics()
    {
        var stl = XElement.Parse(@"<StatementList xmlns='http://www.siemens.com/automation/Openness/SW/NetworkSource/StatementList/v4'>
<StlStatement><StlToken Text='A'/><Access Scope='LocalVariable'><Symbol><Component Name='x'/></Symbol></Access></StlStatement>
<StlStatement><StlToken Text='Rise'/><Access Scope='GlobalVariable'><Symbol><Component Name='M'/><Component Name='edge'/></Symbol></Access></StlStatement>
<StlStatement><StlToken Text='Assign'/><Access Scope='LocalVariable'><Symbol><Component Name='y'/></Symbol></Access><LineComment><Text>pulse</Text></LineComment></StlStatement>
<StlStatement><StlToken Text='EMPTY_LINE'/></StlStatement>
</StatementList>");

        var (text, instructions) = StlRenderer.Render(stl);

        text.Should().Be("A #x\nFP \"M\".edge\n= #y //pulse");
        instructions.Select(i => i.Mnemonic).Should().Equal("A", "FP", "=");
    }

    // ---- helpers -------------------------------------------------------------------

    [Theory]
    [InlineData("Int", "Int", null)]
    [InlineData("\"udt_A\"", "udt_A", null)]
    [InlineData("Array[0..31] of Int", "Int", new[] { 32 })]
    [InlineData("Array[1..2, -1..1] of \"udt_A\"", "udt_A", new[] { 2, 3 })]
    [InlineData("Array[*] of Bool", "Bool", null)]
    public void SplitDatatype_ParsesArrays(string datatype, string type, int[]? lengths)
    {
        var result = SimaticMlProjectBuilder.SplitDatatype(datatype);

        result.DataType.Should().Be(type);
        if (lengths == null)
            result.Lengths.Should().BeNull();
        else
            result.Lengths.Should().Equal(lengths);
    }

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_dir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Block(string type, string language, string networks, string name = "Blk", string extra = "") =>
        $@"<Document><Engineering version='V18'/><SW.Blocks.{type} ID='0'><AttributeList>{extra}<Name>{name}</Name><Number>1</Number>
<ProgrammingLanguage>{language}</ProgrammingLanguage></AttributeList><ObjectList>{networks}</ObjectList></SW.Blocks.{type}></Document>";

    private static string Access(int uid, string scope, string name) =>
        $"<Access Scope='{scope}' UId='{uid}'><Symbol><Component Name='{name}'/></Symbol></Access>";

    private static string Parts(params string[] items) => "<Parts>" + string.Concat(items) + "</Parts>";

    private static string Wire(params string[] ends) => "<Wire>" + string.Concat(ends) + "</Wire>";

    private static (string Text, List<PlcInfo.Models.InstructionInfo> Instructions) RenderLad(string parts, params string[] wires)
    {
        var flgNet = XElement.Parse($"<FlgNet xmlns='{FlgNs}'>{parts}<Wires>{string.Concat(wires)}</Wires></FlgNet>");
        return FlgNetRenderer.Render(flgNet);
    }
}
