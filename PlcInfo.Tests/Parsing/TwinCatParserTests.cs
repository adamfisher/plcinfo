using FluentAssertions;
using PlcInfo.Parsing;
using System.Text;
using Xunit.Categories;

namespace PlcInfo.Tests.Parsing;

[UnitTest]
public class TwinCatParserTests : IDisposable
{
    private readonly TwinCatParser _parser;
    private readonly string _tempDirectory;

    public TwinCatParserTests()
    {
        _parser = new TwinCatParser();
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
    [InlineData("test.tpy", true)]
    [InlineData("test.TPY", true)]
    [InlineData("test.tPy", true)]
    [InlineData("test.tszip", true)]
    [InlineData("test.TSZIP", true)]
    [InlineData("test.tnzip", true)]
    [InlineData("test.TNZIP", true)]
    [InlineData("test.xml", false)]
    [InlineData("test.zip", false)]
    [InlineData("test.L5X", false)]
    public void CanParse_WithVariousExtensions_ReturnsExpectedResult(string filename, bool expected)
    {
        // Act
        var result = _parser.CanParse(filename);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public async Task ParseAsync_WithTwinCatXmlFile_ParsesCorrectly()
    {
        // Arrange
        var twinCatContent = @"<?xml version='1.0' encoding='utf-8'?>
<TcPlcObject Version='1.1.0.1' ProductVersion='3.1.4024.0'>
  <POU Name='MAIN' Id='12345' SpecialFunc='None'>
    <Declaration><![CDATA[
PROGRAM MAIN
VAR
    bStart : BOOL;
    iCounter : INT;
    fbMotor : FB_Motor;
END_VAR
]]></Declaration>
    <Implementation>
      <ST><![CDATA[
IF bStart THEN
    iCounter := iCounter + 1;
    fbMotor(bEnable := TRUE, rSpeed := 100.0);
END_IF
]]></ST>
    </Implementation>
  </POU>
  <POU Name='FB_Motor' Id='67890' SpecialFunc='None'>
    <Declaration><![CDATA[
FUNCTION_BLOCK FB_Motor
VAR_INPUT
    bEnable : BOOL;
    rSpeed : REAL;
END_VAR
VAR_OUTPUT
    bRunning : BOOL;
    rActualSpeed : REAL;
END_VAR
VAR
    tonStartup : TON;
END_VAR
]]></Declaration>
    <Implementation>
      <ST><![CDATA[
tonStartup(IN := bEnable, PT := T#2S);
bRunning := tonStartup.Q;
IF bRunning THEN
    rActualSpeed := rSpeed;
ELSE
    rActualSpeed := 0;
END_IF
]]></ST>
    </Implementation>
  </POU>
  <GlobalTextList Name='GlobalTextList' Id='99999'>
    <XmlArchive>
      <Data>
        <TextList>
          <Text Id='1' Value='Start Motor'/>
          <Text Id='2' Value='Stop Motor'/>
        </TextList>
      </Data>
    </XmlArchive>
  </GlobalTextList>
  <Device Name='PLC1' RemoteConnections='true'>
    <Module Name='CPU'>
      <Properties>
        <Property Name='Type' Value='CX9020'/>
        <Property Name='Version' Value='3.1.4024.0'/>
      </Properties>
    </Module>
  </Device>
</TcPlcObject>";

        var testFile = Path.Combine(_tempDirectory, "test.tpy");
        await File.WriteAllTextAsync(testFile, twinCatContent);

        // Act
        var result = await _parser.ParseAsync(testFile);

        // Assert
        result.Should().NotBeNull();
        result.Vendor.Should().Be("Beckhoff");
        result.Format.Should().Be("TwinCAT");
        result.FilePath.Should().Be(testFile);

        // Controller assertions
        result.Controller.Should().NotBeNull();
        result.Controller!.Name.Should().Be("PLC1");
        result.Controller.Vendor.Should().Be("Beckhoff");
        result.Controller.Type.Should().Be("CX9020");
        result.Controller.FirmwareRevision.Should().Be("3.1.4024.0");

        // Programs assertions
        result.Programs.Should().HaveCount(2);

        var mainProgram = result.Programs.FirstOrDefault(p => p.Name == "MAIN");
        mainProgram.Should().NotBeNull();
        mainProgram!.Type.Should().Be("Program");
        mainProgram.Language.Should().Be("ST");
        mainProgram.LocalTags.Should().HaveCount(3);
        mainProgram.LocalTags.Any(t => t.Name == "bStart" && t.DataType == "BOOL").Should().BeTrue();
        mainProgram.LocalTags.Any(t => t.Name == "iCounter" && t.DataType == "INT").Should().BeTrue();
        mainProgram.LocalTags.Any(t => t.Name == "fbMotor" && t.DataType == "FB_Motor").Should().BeTrue();

        var fbMotor = result.Programs.FirstOrDefault(p => p.Name == "FB_Motor");
        fbMotor.Should().NotBeNull();
        fbMotor!.Type.Should().Be("Function Block");
        fbMotor.LocalTags.Should().HaveCount(5);
        fbMotor.LocalTags.Any(t => t.Name == "bEnable" && t.Usage == "Input").Should().BeTrue();
        fbMotor.LocalTags.Any(t => t.Name == "rSpeed" && t.Usage == "Input").Should().BeTrue();
        fbMotor.LocalTags.Any(t => t.Name == "bRunning" && t.Usage == "Output").Should().BeTrue();
        fbMotor.LocalTags.Any(t => t.Name == "rActualSpeed" && t.Usage == "Output").Should().BeTrue();
        fbMotor.LocalTags.Any(t => t.Name == "tonStartup" && t.DataType == "TON").Should().BeTrue();

        // Metadata
        result.Metadata.Should().ContainKey("ProductVersion");
        result.Metadata["ProductVersion"].Should().Be("3.1.4024.0");
    }

    [Fact]
    public async Task ParseAsync_WithNonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var nonExistentFile = Path.Combine(_tempDirectory, "nonexistent.tpy");

        // Act
        var action = async () => await _parser.ParseAsync(nonExistentFile);

        // Assert
        await action.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage($"File not found: {nonExistentFile}");
    }

    [Fact]
    public async Task ParseAsync_WithInvalidXml_ThrowsException()
    {
        // Arrange
        var invalidContent = "This is not valid XML";
        var testFile = Path.Combine(_tempDirectory, "invalid.tpy");
        await File.WriteAllTextAsync(testFile, invalidContent);

        // Act
        var action = async () => await _parser.ParseAsync(testFile);

        // Assert
        await action.Should().ThrowAsync<System.Xml.XmlException>();
    }

    [Fact]
    public async Task ParseAsync_WithEmptyPouList_ReturnsEmptyPrograms()
    {
        // Arrange
        var twinCatContent = @"<?xml version='1.0' encoding='utf-8'?>
<TcPlcObject Version='1.1.0.1' ProductVersion='3.1.4024.0'>
  <Device Name='PLC1' RemoteConnections='true'>
    <Module Name='CPU'>
      <Properties>
        <Property Name='Type' Value='CX9020'/>
        <Property Name='Version' Value='3.1.4024.0'/>
      </Properties>
    </Module>
  </Device>
</TcPlcObject>";

        var testFile = Path.Combine(_tempDirectory, "empty.tpy");
        await File.WriteAllTextAsync(testFile, twinCatContent);

        // Act
        var result = await _parser.ParseAsync(testFile);

        // Assert
        result.Should().NotBeNull();
        result.Programs.Should().BeEmpty();
        result.Tags.Should().BeEmpty();
    }
}