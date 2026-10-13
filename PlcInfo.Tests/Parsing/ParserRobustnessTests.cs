using System.Text;
using FluentAssertions;
using PlcInfo.Parsing;
using Xunit.Categories;

namespace PlcInfo.Tests.Parsing;

[UnitTest]
public class ParserRobustnessTests : IDisposable
{
    private readonly string _tempDirectory;

    public ParserRobustnessTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    // ---- TwinCAT / IEC 61131-3 declarations ----

    [Fact]
    public void TwinCat_VariableNamedVarSomething_IsNotTreatedAsSectionHeader()
    {
        var tags = TwinCatParser.ParseDeclarationText(@"
VAR_INPUT
    VarCount : INT;
    bEnable : BOOL;
END_VAR", "FB");

        tags.Select(t => (t.Name, t.Usage)).Should().Equal(("VarCount", "Input"), ("bEnable", "Input"));
    }

    [Fact]
    public void TwinCat_CommentsAndPragmas_DoNotProduceTags()
    {
        var tags = TwinCatParser.ParseDeclarationText(@"
VAR
    // old : BOOL;
    (* note : legacy *)
    {attribute 'TcLinkTo' := 'TIID^Device 1^Term 1^Channel 1'}
    bReal : BOOL; // trailing comment
    (* multi-line
       comment : WORD; *)
END_VAR // end of locals", "Main");

        tags.Should().ContainSingle().Which.Name.Should().Be("bReal");
    }

    [Fact]
    public void TwinCat_MultipleNamesOnOneLine_DeclareSeparateTags()
    {
        var tags = TwinCatParser.ParseDeclarationText("VAR\n  a, b, c : BOOL;\nEND_VAR", "Main");

        tags.Select(t => t.Name).Should().Equal("a", "b", "c");
        tags.Should().AllSatisfy(t => t.DataType.Should().Be("BOOL"));
    }

    [Fact]
    public void TwinCat_SemicolonInsideStringInitialValue_IsPreserved()
    {
        var tags = TwinCatParser.ParseDeclarationText("VAR\n  s : STRING := 'x;y';\nEND_VAR", "Main");

        tags.Should().ContainSingle().Which.InitialValue.Should().Be("'x;y'");
    }

    [Fact]
    public void TwinCat_QualifiedSectionsAndLowercaseKeywords_AreRecognised()
    {
        var tags = TwinCatParser.ParseDeclarationText(@"
var_input
    bIn AT %IX0.0 : BOOL;
end_var
VAR CONSTANT
    MAX : INT := 10;
END_VAR
VAR PERSISTENT
    nCount : DINT;
END_VAR", "FB");

        tags.Select(t => (t.Name, t.Usage)).Should().Equal(
            ("bIn", "Input"), ("MAX", "VAR"), ("nCount", "VAR_PERSISTENT"));
        tags[0].Address.Should().Be("%IX0.0");
        tags[0].IsIoTag.Should().BeTrue();
    }

    [Fact]
    public void TwinCat_DeclarationSpanningLines_IsParsed()
    {
        var tags = TwinCatParser.ParseDeclarationText("VAR\n  arr : ARRAY[1..3] OF INT\n      := [1, 2, 3];\nEND_VAR", "Main");

        tags.Should().ContainSingle().Which.DataType.Should().Be("ARRAY[1..3] OF INT");
    }

    [Theory]
    [InlineData("../secret.xml")]
    [InlineData("..\\secret.xml")]
    [InlineData("PLC/../../secret.xml")]
    public void TwinCat_ProjectReferenceEscapingProjectDir_IsRejected(string reference)
    {
        TwinCatParser.ResolveProjectReference(_tempDirectory, reference).Should().BeNull();
    }

    [Fact]
    public void TwinCat_RootedProjectReference_IsRejected()
    {
        var rooted = Path.Combine(Path.GetTempPath(), "secret.xml");

        TwinCatParser.ResolveProjectReference(_tempDirectory, rooted).Should().BeNull();
    }

    [Fact]
    public void TwinCat_BackslashProjectReference_ResolvesInsideProjectDir()
    {
        var resolved = TwinCatParser.ResolveProjectReference(_tempDirectory, "PLC\\PLC.plcproj");

        resolved.Should().Be(Path.Combine(Path.GetFullPath(_tempDirectory), "PLC", "PLC.plcproj"));
    }

    // ---- Siemens ----

    [Fact]
    public void Step7_AwlWithSymbolicHeader_ParsesBlockWithHeaderTitle()
    {
        // Header style as exported by Step 7: a quoted symbol instead of "FB 10".
        var blocks = SiemensStep7Parser.ParseAwlSource(@"FUNCTION_BLOCK ""MOTOR v4.3""
TITLE =Motor with CD and SS sensors
VERSION : 4.3
BEGIN
NETWORK
TITLE =Check which options to use
      A     #Start;
NETWORK
TITLE =Indicate running
      =     #Running;
END_FUNCTION_BLOCK
", "MOTOR.awl");

        var block = blocks.Should().ContainSingle().Subject;
        block.Name.Should().Be("MOTOR v4.3");
        block.Type.Should().Be("FB");
        block.Title.Should().Be("Motor with CD and SS sensors");
        block.NetworkCount.Should().Be(2);
    }

    [Fact]
    public void Step7_AwlWithSeveralBlocks_EmitsOneBlockEach()
    {
        var blocks = SiemensStep7Parser.ParseAwlSource(@"ORGANIZATION_BLOCK OB 1
TITLE = Main cycle
BEGIN
NETWORK
END_ORGANIZATION_BLOCK
function_block fb 10
BEGIN
NETWORK
NETWORK
END_FUNCTION_BLOCK", "multi.awl");

        blocks.Select(b => (b.Name, b.Number, b.NetworkCount)).Should().Equal(("OB1", 1, 1), ("FB10", 10, 2));
        blocks[0].Title.Should().Be("Main cycle");
    }

    [Fact]
    public async Task Step7_ProjectFolderWithWindows1252Awl_ParsesBlock()
    {
        // GetEncoding(1252) threw on .NET until the code-pages provider was registered,
        // and the failure was swallowed, giving an empty project.
        var blocksDir = Path.Combine(_tempDirectory, "s7prog", "BLOCKS");
        Directory.CreateDirectory(blocksDir);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        await File.WriteAllBytesAsync(Path.Combine(blocksDir, "ob1.awl"),
            Encoding.GetEncoding(1252).GetBytes("ORGANIZATION_BLOCK OB 1\r\nTITLE = Füllstand\r\nNETWORK\r\nEND_ORGANIZATION_BLOCK\r\n"));

        var project = await new SiemensStep7Parser().ParseAsync(_tempDirectory);

        project.Blocks.Should().ContainSingle().Which.Title.Should().Be("Füllstand");
    }

    // ---- CODESYS / PLCopen ----

    [Fact]
    public async Task Codesys_DeeplyNestedXml_DoesNotOverflowStack()
    {
        // Recursive namespace stripping used to crash the process (uncatchable).
        const int depth = 20_000;
        var sb = new StringBuilder("<project xmlns='http://www.plcopen.org/xml/tc6_0200'><fileHeader productName='CODESYS'/>");
        for (int i = 0; i < depth; i++) sb.Append("<n>");
        for (int i = 0; i < depth; i++) sb.Append("</n>");
        sb.Append("</project>");
        var path = Path.Combine(_tempDirectory, "deep.xml");
        await File.WriteAllTextAsync(path, sb.ToString());

        var action = async () => await new CodesysParser().ParseAsync(path);

        await action.Should().NotThrowAsync<InsufficientExecutionStackException>();
    }

    [Fact]
    public async Task Codesys_XsiTypeAttribute_DoesNotOverwritePouType()
    {
        var path = Path.Combine(_tempDirectory, "xsi.xml");
        await File.WriteAllTextAsync(path, @"<?xml version='1.0' encoding='utf-8'?>
<project xmlns='http://www.plcopen.org/xml/tc6_0200' xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance'>
  <fileHeader productName='CODESYS' />
  <types><dataTypes/><pous>
    <pou name='Main' pouType='program' xsi:type='somethingElse'><interface/><body><ST><xhtml xmlns='http://www.w3.org/1999/xhtml'>x := 1;</xhtml></ST></body></pou>
  </pous></types>
</project>");

        var project = await new CodesysParser().ParseAsync(path);

        project.Programs.Should().ContainSingle(p => p.Name == "Main").Which.Type.Should().Be("program");
    }
}
