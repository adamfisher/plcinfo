using System.IO.Compression;
using FluentAssertions;
using PlcInfo.Formatters;
using PlcInfo.Parsing;
using PlcInfo.Services;
using Xunit.Categories;

namespace PlcInfo.Tests.Integration;

[IntegrationTest]
public class EndToEndTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly PlcParserRegistry _parserRegistry;
    private readonly MetricsService _metricsService;
    private readonly SearchService _searchService;
    private readonly PlcDiffService _diffService;

    public EndToEndTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);

        var parsers = new IPlcProjectParser[]
        {
            new RockwellL5XParser(),
            new RockwellAcdParser(),
            new CodesysParser(),
            new SiemensStep7Parser(),
            new SiemensTiaPortalParser(),
            new TwinCatParser()
        };

        _parserRegistry = new PlcParserRegistry(parsers);
        _metricsService = new MetricsService();
        _searchService = new SearchService();
        _diffService = new PlcDiffService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    [Fact]
    public async Task ParseAndAnalyzeL5X_CompleteWorkflow()
    {
        // Arrange
        var l5xContent = CreateSampleL5XContent();
        var testFile = Path.Combine(_tempDirectory, "test.L5X");
        await File.WriteAllTextAsync(testFile, l5xContent);

        // Act - Parse
        var parser = _parserRegistry.ResolveForFile(testFile);
        var project = await parser.ParseAsync(testFile);

        // Assert - Parse
        project.Should().NotBeNull();
        project.Vendor.Should().Be("Rockwell Automation");
        project.Programs.Should().NotBeEmpty();
        project.Tags.Should().NotBeEmpty();

        // Act - Metrics
        var metrics = _metricsService.CalculateMetrics(project);

        // Assert - Metrics
        metrics.Should().NotBeNull();
        metrics.TotalPrograms.Should().BeGreaterThan(0);
        metrics.TotalTags.Should().BeGreaterThan(0);
        metrics.ComplexityScore.Should().BeInRange(0, 100);

        // Act - Search
        var searchResults = _searchService.Search(project, "Motor").ToList();

        // Assert - Search
        searchResults.Should().NotBeEmpty();
        searchResults.Should().Contain(r => r.Type == "Tag");

        // Act - Format
        var jsonFormatter = new JsonOutputFormatter();
        var textFormatter = new TextOutputFormatter();
        var markdownFormatter = new MarkdownOutputFormatter();

        var jsonOutput = jsonFormatter.FormatSummary(project);
        var textOutput = textFormatter.FormatSummary(project);
        var markdownOutput = markdownFormatter.FormatMetrics(metrics);

        // Assert - Format
        jsonOutput.Should().NotBeNullOrEmpty();
        jsonOutput.Should().Contain("\"vendor\"");

        textOutput.Should().NotBeNullOrEmpty();
        textOutput.Should().Contain("Rockwell Automation");

        markdownOutput.Should().NotBeNullOrEmpty();
        markdownOutput.Should().Contain("# Project Metrics");
    }

    [Fact]
    public async Task ParseAndAnalyzeCodesys_CompleteWorkflow()
    {
        // Arrange
        var codesysContent = CreateSampleCodesysContent();
        var testFile = Path.Combine(_tempDirectory, "test.export");
        await File.WriteAllTextAsync(testFile, codesysContent);

        // Act - Parse
        var parser = _parserRegistry.ResolveForFile(testFile);
        var project = await parser.ParseAsync(testFile);

        // Assert - Parse
        project.Should().NotBeNull();
        project.Vendor.Should().Be("CODESYS");
        project.Programs.Should().NotBeEmpty();

        // Act - Metrics
        var metrics = _metricsService.CalculateMetrics(project);

        // Assert - Metrics
        metrics.TotalPrograms.Should().BeGreaterThan(0);
        metrics.LanguageDistribution.Should().ContainKey("ST");

        // Act - Search
        var searchResults = _searchService.Search(project, "Start", "tag").ToList();

        // Assert - Search
        searchResults.Should().NotBeEmpty();

        // Act - Format
        var yamlFormatter = new YamlOutputFormatter();
        var yamlOutput = yamlFormatter.FormatStructure(project);

        // Assert - Format
        yamlOutput.Should().NotBeNullOrEmpty();
        yamlOutput.Should().Contain("structure:");
    }

    [Fact]
    public async Task CompareTwoProjects_DetectsDifferences()
    {
        // Arrange
        var l5xContent1 = CreateSampleL5XContent();
        var l5xContent2 = CreateModifiedL5XContent();

        var testFile1 = Path.Combine(_tempDirectory, "project1.L5X");
        var testFile2 = Path.Combine(_tempDirectory, "project2.L5X");

        await File.WriteAllTextAsync(testFile1, l5xContent1);
        await File.WriteAllTextAsync(testFile2, l5xContent2);

        var parser = new RockwellL5XParser();

        // Act - Parse both projects
        var project1 = await parser.ParseAsync(testFile1);
        var project2 = await parser.ParseAsync(testFile2);

        // Act - Compare
        var diffResult = _diffService.Compare(project1, project2);

        // Assert - Compare
        diffResult.Should().NotBeNull();
        diffResult.HasDifferences.Should().BeTrue();
        diffResult.Details.Should().NotBeEmpty();

        // Act - Format diff
        var formatter = new MarkdownOutputFormatter();
        var diffOutput = formatter.FormatDiff(diffResult);

        // Assert - Format
        diffOutput.Should().NotBeNullOrEmpty();
        diffOutput.Should().Contain("# Diff Results");
    }

    [Fact]
    public async Task MultipleFormatsInRegistry_ResolvesCorrectParser()
    {
        // Arrange
        var files = new[]
        {
            ("test.L5X", "Rockwell Automation"),
            ("test.export", "CODESYS"),
            ("test.tsproj", "Beckhoff"),
            ("test.ap17", "Siemens"),
            ("test.s7p", "Siemens")
        };

        // Act & Assert
        foreach (var (filename, expectedVendor) in files)
        {
            var parser = _parserRegistry.ResolveForFile(filename);
            parser.Should().NotBeNull();
            parser.CanParse(filename).Should().BeTrue();

            // Create a minimal file for testing
            var testFile = Path.Combine(_tempDirectory, filename);

            if (filename.EndsWith(".tsproj"))
            {
                // TwinCAT rejects XML that is not a TwinCAT project instead of returning an empty one.
                await File.WriteAllTextAsync(testFile, "<?xml version='1.0'?><TcSmProject/>");
            }
            else if (filename.EndsWith(".L5X") || filename.EndsWith(".export"))
            {
                await File.WriteAllTextAsync(testFile, "<?xml version='1.0'?><root/>");
            }
            else
            {
                // Create a minimal valid ZIP archive for TIA Portal files
                using (var archive = ZipFile.Open(testFile, ZipArchiveMode.Create))
                {
                    archive.CreateEntry("dummy.txt");
                }
            }

            var project = await parser.ParseAsync(testFile);
            project.Should().NotBeNull();
            project.Vendor.Should().Be(expectedVendor);
        }

        // .ACD resolves to the ACD parser, which rejects a file that is not a real ACD
        // (an ACD is not a ZIP archive) instead of returning an empty project.
        var acdParser = _parserRegistry.ResolveForFile("test.acd");
        acdParser.Should().BeOfType<RockwellAcdParser>();
        var acdFile = Path.Combine(_tempDirectory, "test.acd");
        using (var archive = ZipFile.Open(acdFile, ZipArchiveMode.Create))
        {
            archive.CreateEntry("dummy.txt");
        }
        var parseAcd = async () => await acdParser.ParseAsync(acdFile);
        await parseAcd.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void UnsupportedFileFormat_ThrowsException()
    {
        // Arrange & Act
        var action = () => _parserRegistry.ResolveForFile("test.unknown");

        // Assert
        action.Should().Throw<NotSupportedException>()
            .WithMessage("No parser found for file: test.unknown");
    }

    private string CreateSampleL5XContent()
    {
        return @"<?xml version='1.0' encoding='UTF-8' standalone='yes'?>
<RSLogix5000Content SchemaRevision='1.0' SoftwareRevision='32.00'>
  <Controller Name='MainController' ProcessorType='1756-L85E'>
    <Tags>
      <Tag Name='Motor_Start' TagType='Base' DataType='BOOL'/>
      <Tag Name='Motor_Stop' TagType='Base' DataType='BOOL'/>
      <Tag Name='Motor_Speed' TagType='Base' DataType='INT'/>
      <Tag Name='System_Status' TagType='Base' DataType='DINT'/>
    </Tags>
    <Programs>
      <Program Name='MainProgram'>
        <Tags>
          <Tag Name='Local_Counter' TagType='Base' DataType='INT' Usage='Local'/>
        </Tags>
        <Routines>
          <Routine Name='MainRoutine' Type='RLL'>
            <RLLContent>
              <Rung Number='0' Type='N'>
                <Text>XIC(Motor_Start)OTE(Motor_Run);</Text>
                <Comment>Start the motor</Comment>
              </Rung>
            </RLLContent>
          </Routine>
        </Routines>
      </Program>
    </Programs>
    <Tasks>
      <Task Name='MainTask' Type='CONTINUOUS' Priority='10'>
        <ScheduledPrograms>
          <ScheduledProgram Name='MainProgram'/>
        </ScheduledPrograms>
      </Task>
    </Tasks>
    <Modules>
      <Module Name='Input_Module' CatalogNumber='1756-IB32'/>
      <Module Name='Output_Module' CatalogNumber='1756-OB32'/>
    </Modules>
  </Controller>
</RSLogix5000Content>";
    }

    private string CreateModifiedL5XContent()
    {
        return @"<?xml version='1.0' encoding='UTF-8' standalone='yes'?>
<RSLogix5000Content SchemaRevision='1.0' SoftwareRevision='33.00'>
  <Controller Name='MainController' ProcessorType='1756-L85E'>
    <Tags>
      <Tag Name='Motor_Start' TagType='Base' DataType='BOOL'/>
      <Tag Name='Motor_Stop' TagType='Base' DataType='BOOL'/>
      <Tag Name='Motor_Speed' TagType='Base' DataType='REAL'/>
      <Tag Name='System_Status' TagType='Base' DataType='DINT'/>
      <Tag Name='New_Tag' TagType='Base' DataType='STRING'/>
    </Tags>
    <Programs>
      <Program Name='MainProgram'>
        <Tags>
          <Tag Name='Local_Counter' TagType='Base' DataType='INT' Usage='Local'/>
          <Tag Name='Local_Timer' TagType='Base' DataType='TIMER' Usage='Local'/>
        </Tags>
        <Routines>
          <Routine Name='MainRoutine' Type='RLL'>
            <RLLContent>
              <Rung Number='0' Type='N'>
                <Text>XIC(Motor_Start)OTE(Motor_Run);</Text>
                <Comment>Start the motor with new logic</Comment>
              </Rung>
              <Rung Number='1' Type='N'>
                <Text>XIO(Motor_Stop)OTU(Motor_Run);</Text>
              </Rung>
            </RLLContent>
          </Routine>
        </Routines>
      </Program>
    </Programs>
    <Tasks>
      <Task Name='MainTask' Type='CONTINUOUS' Priority='10'>
        <ScheduledPrograms>
          <ScheduledProgram Name='MainProgram'/>
        </ScheduledPrograms>
      </Task>
    </Tasks>
    <Modules>
      <Module Name='Input_Module' CatalogNumber='1756-IB32'/>
      <Module Name='Output_Module' CatalogNumber='1756-OB32'/>
      <Module Name='Analog_Module' CatalogNumber='1756-IF8'/>
    </Modules>
  </Controller>
</RSLogix5000Content>";
    }

    private string CreateSampleCodesysContent()
    {
        return @"<?xml version='1.0' encoding='utf-8'?>
<project xmlns='http://www.plcopen.org/xml/tc6_0201'>
  <fileHeader companyName='TestCompany' productName='CODESYS' productVersion='3.5.16.0'/>
  <contentHeader name='TestProject'/>
  <types>
    <pous>
      <pou name='MAIN' pouType='program'>
        <interface>
          <localVars>
            <variable name='Start'>
              <type><BOOL/></type>
            </variable>
            <variable name='Counter'>
              <type><INT/></type>
            </variable>
          </localVars>
        </interface>
        <body>
          <ST>
            <xhtml xmlns='http://www.w3.org/1999/xhtml'>
IF Start THEN
    Counter := Counter + 1;
END_IF
            </xhtml>
          </ST>
        </body>
      </pou>
    </pous>
  </types>
  <instances>
    <configurations>
      <configuration name='Config1'>
        <resource name='Resource1'>
          <task name='MainTask' priority='1' interval='T#10ms'>
            <pouInstance name='MAIN'/>
          </task>
        </resource>
      </configuration>
    </configurations>
  </instances>
</project>";
    }
}