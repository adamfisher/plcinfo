using FluentAssertions;
using PlcInfo.Parsing;
using Xunit.Categories;

namespace PlcInfo.Tests.Parsing;

[UnitTest]
public class TwinCatParserTests : IDisposable
{
    private readonly TwinCatParser _parser = new();
    private readonly string _tempDirectory;

    public TwinCatParserTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, true);
    }

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_tempDirectory, relativePath.Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string PlcProj(params string[] includes) =>
        "<Project DefaultTargets=\"Build\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">" +
        "<PropertyGroup><Name>PLC</Name><ProgramVersion>3.1.4024.0</ProgramVersion></PropertyGroup><ItemGroup>" +
        string.Concat(includes.Select(i => $"<Compile Include=\"{i}\"><SubType>Code</SubType></Compile>")) +
        "</ItemGroup></Project>";

    private static string Pou(string name, string declaration, string body = "<ST><![CDATA[;]]></ST>", string children = "") =>
        $"<?xml version=\"1.0\" encoding=\"utf-8\"?><TcPlcObject Version=\"1.1.0.1\" ProductVersion=\"3.1.4024.0\">" +
        $"<POU Name=\"{name}\" Id=\"{{1}}\" SpecialFunc=\"None\"><Declaration><![CDATA[{declaration}]]></Declaration>" +
        $"<Implementation>{body}</Implementation>{children}</POU></TcPlcObject>";

    private static string Tsproj(string plc, string tasks = "") =>
        "<?xml version=\"1.0\"?><TcSmProject TcSmVersion=\"1.0\" TcVersion=\"3.1.4024.12\">" +
        $"<Project ProjectGUID=\"{{G}}\" TargetNetId=\"1.2.3.4.1.1\"><System><Tasks>{tasks}</Tasks></System>" +
        $"<Plc>{plc}</Plc></Project></TcSmProject>";

    [Theory]
    [InlineData("test.tsproj", true)]
    [InlineData("test.plcproj", true)]
    [InlineData("MAIN.TcPOU", true)]
    [InlineData("GVL.TcGVL", true)]
    [InlineData("ST_X.TcDUT", true)]
    [InlineData("I_X.TcIO", true)]
    [InlineData("PlcTask.TcTTO", true)]
    [InlineData("test.tszip", true)]
    [InlineData("test.TNZIP", true)]
    [InlineData("test.xml", false)]
    [InlineData("test.zip", false)]
    [InlineData("test.L5X", false)]
    public void CanParse_WithVariousExtensions_ReturnsExpectedResult(string filename, bool expected)
    {
        _parser.CanParse(filename).Should().Be(expected);
    }

    [Fact]
    public async Task Tsproj_ResolvesPrjFilePath_AndConvertsTaskUnits()
    {
        Write("PLC\\PLC.plcproj", PlcProj("POUs\\MAIN.TcPOU", "PlcTask.TcTTO"));
        Write("PLC\\POUs\\MAIN.TcPOU", Pou("MAIN", "PROGRAM MAIN\nVAR\n  bStart : BOOL;\nEND_VAR"));
        Write("PLC\\PlcTask.TcTTO",
            "<TcPlcObject><Task Name=\"PlcTask\" Id=\"{t}\"><CycleTime>2000</CycleTime><Priority>20</Priority>" +
            "<PouCall><Name>MAIN</Name></PouCall></Task></TcPlcObject>");
        var tsproj = Write("Sys.tsproj", Tsproj(
            "<Project Name=\"PLC\" PrjFilePath=\"PLC\\PLC.plcproj\" AmsPort=\"851\"/>",
            "<Task Id=\"2\" Priority=\"20\" CycleTime=\"20000\"><Name>PlcTask</Name></Task>"));

        var project = await _parser.ParseAsync(tsproj);

        project.Metadata.Should().NotContainKey("Warnings");
        project.Programs.Should().ContainSingle().Which.TaskName.Should().Be("PlcTask");
        // tsproj CycleTime 20000 × 100 ns = 2 ms, which agrees with the .TcTTO's 2000 µs.
        var task = project.Tasks.Should().ContainSingle().Subject;
        task.Rate.Should().Be(2.0);
        task.ProgramNames.Should().Equal("MAIN");
    }

    [Fact]
    public async Task TaskObject_CycleTimeIsMicroseconds()
    {
        var tto = Write("PlcTask.TcTTO",
            "<TcPlcObject><Task Name=\"Fast\"><CycleTime>500</CycleTime><Priority>5</Priority></Task></TcPlcObject>");

        var project = await _parser.ParseAsync(tto);

        project.Tasks.Single().Rate.Should().Be(0.5);
        project.Tasks.Single().Priority.Should().Be(5);
    }

    [Fact]
    public async Task Tsproj_IndependentProjectFile_FollowsXtiReference()
    {
        Write("_Config\\PLC\\Lib.xti",
            "<TcSmItem ClassName=\"CNestedPlcProjDef\"><Project Name=\"Lib\" PrjFilePath=\"..\\..\\Lib\\Lib.plcproj\"/></TcSmItem>");
        Write("Lib\\Lib.plcproj", PlcProj("POUs\\FB_A.TcPOU"));
        Write("Lib\\POUs\\FB_A.TcPOU", Pou("FB_A", "FUNCTION_BLOCK FB_A"));
        var tsproj = Write("Lib.tsproj", Tsproj("<Project File=\"Lib.xti\"/>"));

        var project = await _parser.ParseAsync(tsproj);

        project.Metadata.Should().NotContainKey("Warnings");
        project.Programs.Should().ContainSingle().Which.Type.Should().Be("FunctionBlock");
    }

    [Fact]
    public async Task Tsproj_MissingPlcProject_IsReportedAsWarning()
    {
        var tsproj = Write("Sys.tsproj", Tsproj("<Project Name=\"PLC\" PrjFilePath=\"PLC\\PLC.plcproj\"/>"));

        var project = await _parser.ParseAsync(tsproj);

        project.Programs.Should().BeEmpty();
        project.Metadata["Warnings"].Should().Be("PLC project file 'PLC\\PLC.plcproj' was not found.");
    }

    [Fact]
    public async Task Tsproj_PlcProjectOutsideProjectDir_IsNotRead()
    {
        var tsproj = Write("Sys\\Sys.tsproj", Tsproj("<Project Name=\"PLC\" PrjFilePath=\"..\\PLC.plcproj\"/>"));
        Write("PLC.plcproj", PlcProj());

        var project = await _parser.ParseAsync(tsproj);

        project.Metadata["Warnings"].Should().Contain("points outside the project directory");
    }

    [Fact]
    public async Task Plcproj_MissingMalformedAndEscapingSources_WarnAndContinue()
    {
        var plcproj = Write("PLC\\PLC.plcproj",
            PlcProj("POUs\\Good.TcPOU", "POUs\\Missing.TcPOU", "POUs\\Broken.TcPOU", "..\\..\\Evil.TcPOU", "Visu.TcVIS"));
        Write("PLC\\POUs\\Good.TcPOU", Pou("Good", "FUNCTION Good : INT"));
        Write("PLC\\POUs\\Broken.TcPOU", "<TcPlcObject><POU Name=\"Broken\">");

        var project = await _parser.ParseAsync(plcproj);

        project.Programs.Should().ContainSingle().Which.Should().Match<PlcInfo.Models.ProgramInfo>(p =>
            p.Name == "Good" && p.Type == "Function" && p.Properties["ReturnType"] == "INT");
        var warnings = project.Metadata["Warnings"].Split('\n');
        warnings.Should().HaveCount(3);
        warnings[0].Should().Be("PLC project 'PLC': source file 'POUs\\Missing.TcPOU' was not found.");
        warnings[1].Should().StartWith("PLC project 'PLC': source file 'POUs\\Broken.TcPOU' could not be read:");
        warnings[2].Should().Be("PLC project 'PLC': source '..\\..\\Evil.TcPOU' was skipped because it points outside the project directory.");
    }

    [Fact]
    public async Task GraphicalBody_RecordsLanguageWithoutSource()
    {
        var file = Write("SFC.TcPOU", Pou("SeqMain", "PROGRAM SeqMain",
            body: "<SFC><XmlArchive><Data/></XmlArchive></SFC>",
            children: "<Action Name=\"Reset\" Id=\"{a}\"><Implementation><ST><![CDATA[x := FALSE;]]></ST></Implementation></Action>" +
                      "<Transition Name=\"T1\" Id=\"{t}\"><Implementation><ST><![CDATA[x]]></ST></Implementation></Transition>"));

        var program = (await _parser.ParseAsync(file)).Programs.Single();

        program.Language.Should().Be("SFC");
        var main = program.Routines[0];
        main.Language.Should().Be("SFC");
        main.Source.Should().BeNull();
        main.Properties["Graphical"].Should().Be("true");
        program.Routines.Skip(1).Select(r => (r.Name, r.Type, r.Source)).Should().Equal(
            ("Reset", "Action", "x := FALSE;"), ("T1", "Transition", "x"));
    }

    [Fact]
    public async Task Pou_HeaderModifiersExtendsAndImplements_AreRecorded()
    {
        var file = Write("FB.TcPOU", Pou("FB_Axis",
            "{attribute 'reflection'}\n(* a (* nested *) comment *)\nFUNCTION_BLOCK PUBLIC ABSTRACT FB_Axis EXTENDS FB_Base IMPLEMENTS I_A, I_B\nVAR_INPUT a : INT; END_VAR"));

        var program = (await _parser.ParseAsync(file)).Programs.Single();

        program.Type.Should().Be("FunctionBlock");
        program.Properties["Extends"].Should().Be("FB_Base");
        program.Properties["Implements"].Should().Be("I_A, I_B");
        program.Properties["Modifiers"].Should().Be("PUBLIC ABSTRACT");
        program.LocalTags.Should().ContainSingle().Which.Usage.Should().Be("Input");
    }

    [Fact]
    public async Task Gvl_TagsUseGvlNameAsScope()
    {
        var file = Write("GVL_Main.TcGVL",
            "<TcPlcObject><GVL Name=\"GVL_Main\"><Declaration><![CDATA[{attribute 'qualified_only'}\nVAR_GLOBAL RETAIN\n  nCount : DINT := 5; // parts made\nEND_VAR]]></Declaration></GVL></TcPlcObject>");

        var project = await _parser.ParseAsync(file);

        project.Programs.Should().BeEmpty();
        var tag = project.Tags.Should().ContainSingle().Subject;
        (tag.Scope, tag.Usage, tag.InitialValue, tag.Description).Should().Be(("GVL_Main", "VAR_GLOBAL", "5", "parts made"));
        tag.Properties["Retain"].Should().Be("true");
    }

    [Theory]
    [InlineData("TYPE T_Name : STRING(80); END_TYPE", "Alias", "STRING(80)")]
    [InlineData("TYPE T_Pct : INT (0..100); END_TYPE", "Alias", "INT (0..100)")]
    [InlineData("TYPE ST_B EXTENDS ST_A :\nSTRUCT\n  x : REAL;\nEND_STRUCT\nEND_TYPE", "Struct", "ST_A")]
    public void Dut_KindAndBaseType(string declaration, string kind, string baseType)
    {
        var dataType = TwinCatParser.ParseDutDeclaration(declaration)!;

        dataType.Kind.Should().Be(kind);
        dataType.BaseType.Should().Be(baseType);
    }

    [Fact]
    public void Dut_StructMembers_KeepArrayBoundsInitialValuesAndComments()
    {
        var dataType = TwinCatParser.ParseDutDeclaration(
            "TYPE ST_X :\nSTRUCT\n  arr : ARRAY[1..3, 0..1] OF INT := [6(0)]; (* samples *)\n  s : STRING := 'a;b';\nEND_STRUCT\nEND_TYPE")!;

        dataType.Members.Select(m => (m.Name, m.DataType, m.Dimensions, m.InitialValue, m.Description)).Should().Equal(
            ("arr", "ARRAY[1..3, 0..1] OF INT", "1..3,0..1", "[6(0)]", "samples"),
            ("s", "STRING", null, "'a;b'", null));
    }

    [Fact]
    public void Dut_EnumImplicitValuesContinueFromExplicitOnes()
    {
        var dataType = TwinCatParser.ParseDutDeclaration("TYPE E : (A, B := 16#10, C, D := 5, E) DINT; END_TYPE")!;

        dataType.BaseType.Should().Be("DINT");
        dataType.Members.Select(m => (m.Name, m.InitialValue)).Should().Equal(
            ("A", "0"), ("B", "16#10"), ("C", "17"), ("D", "5"), ("E", "6"));
    }

    [Fact]
    public async Task Dut_WithoutTypeBlock_IsWarned()
    {
        var file = Write("Bad.TcDUT", "<TcPlcObject><DUT Name=\"Bad\"><Declaration><![CDATA[STRUCT x : INT; END_STRUCT]]></Declaration></DUT></TcPlcObject>");

        var project = await _parser.ParseAsync(file);

        project.DataTypes.Should().BeEmpty();
        project.Metadata["Warnings"].Should().Be("Data type 'Bad' in 'Bad.TcDUT' has no TYPE ... END_TYPE declaration and was skipped.");
    }

    [Fact]
    public void Declaration_OneLineSectionsAndNestedComments()
    {
        var tags = TwinCatParser.ParseDeclarationText(
            "METHOD M : BOOL\nVAR_INPUT a : DINT; b : DINT; END_VAR\nVAR_OUTPUT (* out (* nested *) *) sum : DINT; END_VAR", "M");

        tags.Select(t => (t.Name, t.Usage)).Should().Equal(("a", "Input"), ("b", "Input"), ("sum", "Output"));
    }

    [Fact]
    public async Task Archive_IsRejectedWithGuidance()
    {
        var file = Write("Project.tszip", "PK\u0003\u0004rest");

        var act = () => _parser.ParseAsync(file);

        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*Extract it and pass the .tsproj*");
    }

    [Fact]
    public async Task InvalidXml_ThrowsClearError()
    {
        var file = Write("invalid.TcPOU", "This is not valid XML");

        var act = () => _parser.ParseAsync(file);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("invalid.TcPOU is not a valid TwinCAT XML file*");
    }

    [Fact]
    public async Task UnknownRoot_IsNotSupported()
    {
        var file = Write("other.tsproj", "<Something/>");

        var act = () => _parser.ParseAsync(file);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task NonExistentFile_ThrowsFileNotFoundException()
    {
        var missing = Path.Combine(_tempDirectory, "nonexistent.tsproj");

        var act = () => _parser.ParseAsync(missing);

        await act.Should().ThrowAsync<FileNotFoundException>().WithMessage($"File not found: {missing}");
    }
}
