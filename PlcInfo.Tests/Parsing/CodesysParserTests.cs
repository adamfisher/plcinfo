using FluentAssertions;
using PlcInfo.Parsing;
using System.Text;
using Xunit.Categories;

namespace PlcInfo.Tests.Parsing;

[UnitTest]
public class CodesysParserTests : IDisposable
{
    private readonly CodesysParser _parser;
    private readonly string _tempDirectory;

    public CodesysParserTests()
    {
        _parser = new CodesysParser();
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

    [Theory]
    [InlineData("test.project", true)]
    [InlineData("test.PROJECT", true)]
    [InlineData("test.export", true)]
    [InlineData("test.EXPORT", true)]
    [InlineData("test.library", true)]
    [InlineData("test.xml", false)]
    [InlineData("test.L5X", false)]
    [InlineData("test.txt", false)]
    public void CanParse_WithVariousExtensions_ReturnsExpectedResult(string filename, bool expected)
    {
        // Act
        var result = _parser.CanParse(filename);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public async Task ParseAsync_WithValidCodesysXml_ParsesCorrectly()
    {
        // Arrange
        var codesysContent = @"<?xml version='1.0' encoding='utf-8'?>
<project xmlns='http://www.plcopen.org/xml/tc6_0201'>
  <fileHeader companyName='TestCompany' productName='CODESYS' productVersion='3.5.16.0' creationDateTime='2023-01-01T12:00:00'/>
  <contentHeader name='TestProject' modificationDateTime='2023-01-02T12:00:00'>
    <coordinateInfo>
      <fbd>
        <scaling x='1' y='1'/>
      </fbd>
      <ld>
        <scaling x='1' y='1'/>
      </ld>
      <sfc>
        <scaling x='1' y='1'/>
      </sfc>
    </coordinateInfo>
  </contentHeader>
  <types>
    <dataTypes/>
    <pous>
      <pou name='PLC_PRG' pouType='program'>
        <interface>
          <localVars>
            <variable name='bStart'>
              <type>
                <BOOL/>
              </type>
            </variable>
            <variable name='iCounter'>
              <type>
                <INT/>
              </type>
            </variable>
          </localVars>
        </interface>
        <body>
          <ST>
            <xhtml xmlns='http://www.w3.org/1999/xhtml'>
IF bStart THEN
    iCounter := iCounter + 1;
END_IF
            </xhtml>
          </ST>
        </body>
      </pou>
      <pou name='FB_Motor' pouType='functionBlock'>
        <interface>
          <inputVars>
            <variable name='bEnable'>
              <type>
                <BOOL/>
              </type>
            </variable>
            <variable name='rSpeed'>
              <type>
                <REAL/>
              </type>
            </variable>
          </inputVars>
          <outputVars>
            <variable name='bRunning'>
              <type>
                <BOOL/>
              </type>
            </variable>
          </outputVars>
        </interface>
        <body>
          <ST>
            <xhtml xmlns='http://www.w3.org/1999/xhtml'>
bRunning := bEnable AND (rSpeed > 0);
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
            <pouInstance name='PLC_PRG'/>
          </task>
          <globalVars>
            <variable name='GlobalVar1'>
              <type>
                <DINT/>
              </type>
            </variable>
          </globalVars>
        </resource>
      </configuration>
    </configurations>
  </instances>
</project>";

        var testFile = Path.Combine(_tempDirectory, "test.export");
        await File.WriteAllTextAsync(testFile, codesysContent);

        // Act
        var result = await _parser.ParseAsync(testFile);

        // Assert
        result.Should().NotBeNull();
        result.Vendor.Should().Be("CODESYS");
        result.Format.Should().Be("PLCopen XML (tc6_0201)");
        result.FilePath.Should().Be(testFile);

        // Programs assertions
        result.Programs.Should().HaveCount(2);

        var plcPrg = result.Programs.FirstOrDefault(p => p.Name == "PLC_PRG");
        plcPrg.Should().NotBeNull();
        plcPrg!.Type.Should().Be("Program");
        plcPrg.Language.Should().Be("ST");
        plcPrg.LocalTags.Should().HaveCount(2);
        plcPrg.LocalTags.Any(t => t.Name == "bStart" && t.DataType == "BOOL").Should().BeTrue();
        plcPrg.LocalTags.Any(t => t.Name == "iCounter" && t.DataType == "INT").Should().BeTrue();

        var fbMotor = result.Programs.FirstOrDefault(p => p.Name == "FB_Motor");
        fbMotor.Should().NotBeNull();
        fbMotor!.Type.Should().Be("FunctionBlock");
        fbMotor.LocalTags.Should().HaveCount(3);
        fbMotor.LocalTags.Any(t => t.Name == "bEnable" && t.Usage == "Input").Should().BeTrue();
        fbMotor.LocalTags.Any(t => t.Name == "rSpeed" && t.Usage == "Input").Should().BeTrue();
        fbMotor.LocalTags.Any(t => t.Name == "bRunning" && t.Usage == "Output").Should().BeTrue();

        // Global tags
        result.Tags.Should().HaveCount(1);
        result.Tags[0].Name.Should().Be("GlobalVar1");
        result.Tags[0].DataType.Should().Be("DINT");
        result.Tags[0].Scope.Should().Be("Global");

        // Tasks
        result.Tasks.Should().HaveCount(1);
        result.Tasks[0].Name.Should().Be("MainTask");
        result.Tasks[0].Priority.Should().Be(1);
        result.Tasks[0].Type.Should().Be("Periodic");
        result.Tasks[0].Rate.Should().Be(10); // 10ms interval
        result.Tasks[0].ProgramNames.Should().Equal("PLC_PRG");
        plcPrg.TaskName.Should().Be("MainTask");
        plcPrg.Routines.Single().Source.Should().Contain("iCounter := iCounter + 1;");

        // Metadata
        result.Metadata.Should().ContainKey("CompanyName");
        result.Metadata["CompanyName"].Should().Be("TestCompany");
        result.Metadata.Should().ContainKey("ProductVersion");
        result.Metadata["ProductVersion"].Should().Be("3.5.16.0");
    }

    [Fact]
    public async Task ParseAsync_WithNonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var nonExistentFile = Path.Combine(_tempDirectory, "nonexistent.project");

        // Act
        var action = async () => await _parser.ParseAsync(nonExistentFile);

        // Assert
        await action.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage($"File not found: {nonExistentFile}");
    }

    [Fact]
    public async Task ParseAsync_WithInvalidXml_ThrowsInvalidOperationException()
    {
        // Arrange
        var invalidContent = "This is not valid XML";
        var testFile = Path.Combine(_tempDirectory, "invalid.export");
        await File.WriteAllTextAsync(testFile, invalidContent);

        // Act
        var action = async () => await _parser.ParseAsync(testFile);

        // Assert
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not well-formed XML*");
    }

    [Fact]
    public async Task ParseAsync_WithEmptyPouList_ReturnsEmptyPrograms()
    {
        // Arrange
        var codesysContent = @"<?xml version='1.0' encoding='utf-8'?>
<project xmlns='http://www.plcopen.org/xml/tc6_0201'>
  <fileHeader companyName='TestCompany' productName='CODESYS' productVersion='3.5.16.0' creationDateTime='2023-01-01T12:00:00'/>
  <contentHeader name='EmptyProject' modificationDateTime='2023-01-02T12:00:00'/>
  <types>
    <dataTypes/>
    <pous/>
  </types>
  <instances>
    <configurations/>
  </instances>
</project>";

        var testFile = Path.Combine(_tempDirectory, "empty.export");
        await File.WriteAllTextAsync(testFile, codesysContent);

        // Act
        var result = await _parser.ParseAsync(testFile);

        // Assert
        result.Should().NotBeNull();
        result.Programs.Should().BeEmpty();
        result.Tags.Should().BeEmpty();
        result.Tasks.Should().BeEmpty();
    }
}