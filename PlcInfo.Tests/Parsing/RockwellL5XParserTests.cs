using FluentAssertions;
using PlcInfo.Parsing;
using System.Text;
using Xunit.Categories;

namespace PlcInfo.Tests.Parsing;

[UnitTest]
public class RockwellL5XParserTests : IDisposable
{
    private readonly RockwellL5XParser _parser;
    private readonly string _tempDirectory;

    public RockwellL5XParserTests()
    {
        _parser = new RockwellL5XParser();
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
    [InlineData("test.L5X", true)]
    [InlineData("test.l5x", true)]
    [InlineData("test.L5x", true)]
    [InlineData("test.acd", false)]
    [InlineData("test.xml", false)]
    [InlineData("test.txt", false)]
    public void CanParse_WithVariousExtensions_ReturnsExpectedResult(string filename, bool expected)
    {
        // Act
        var result = _parser.CanParse(filename);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public async Task ParseAsync_WithNonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var nonExistentFile = Path.Combine(_tempDirectory, "nonexistent.L5X");

        // Act
        var action = async () => await _parser.ParseAsync(nonExistentFile);

        // Assert
        await action.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage($"File not found: {nonExistentFile}");
    }

    [Fact]
    public async Task ParseAsync_WithValidL5XFile_ParsesCorrectly()
    {
        // Arrange
        var l5xContent = @"<?xml version='1.0' encoding='UTF-8' standalone='yes'?>
<RSLogix5000Content SchemaRevision='1.0' SoftwareRevision='32.00' TargetType='Controller'>
  <Controller Name='TestController' ProcessorType='1756-L85E' MajorRev='32' MinorRev='11'>
    <Tasks>
      <Task Name='MainTask' Type='CONTINUOUS' Priority='10'>
        <ScheduledPrograms>
          <ScheduledProgram Name='MainProgram'/>
        </ScheduledPrograms>
      </Task>
    </Tasks>
    <Programs>
      <Program Name='MainProgram'>
        <Tags>
          <Tag Name='LocalTag1' TagType='Base' DataType='DINT' Usage='Local'>
            <Data>0</Data>
          </Tag>
        </Tags>
        <Routines>
          <Routine Name='MainRoutine' Type='RLL'>
            <RLLContent>
              <Rung Number='0' Type='N'>
                <Text>XIC(StartButton)OTE(Motor1);</Text>
                <Comment>Start motor when button pressed</Comment>
              </Rung>
              <Rung Number='1' Type='N'>
                <Text>XIO(StopButton)OTU(Motor1);</Text>
              </Rung>
            </RLLContent>
          </Routine>
        </Routines>
      </Program>
    </Programs>
    <Modules>
      <Module Name='Local_IO' CatalogNumber='1756-IB16'>
        <Ports>
          <Port Id='1' Address='0' Type='ICP'/>
        </Ports>
      </Module>
    </Modules>
  </Controller>
</RSLogix5000Content>";

        var testFile = Path.Combine(_tempDirectory, "test.L5X");
        await File.WriteAllTextAsync(testFile, l5xContent);

        // Act
        var result = await _parser.ParseAsync(testFile);

        // Assert
        result.Should().NotBeNull();
        result.Vendor.Should().Be("Rockwell Automation");
        result.Format.Should().Be("L5X");
        result.FilePath.Should().Be(testFile);

        // Controller assertions
        result.Controller.Should().NotBeNull();
        result.Controller!.Name.Should().Be("TestController");
        result.Controller.Type.Should().Be("1756-L85E");
        result.Controller.FirmwareRevision.Should().Be("32.11");

        // Task assertions
        result.Tasks.Should().HaveCount(1);
        result.Tasks[0].Name.Should().Be("MainTask");
        result.Tasks[0].Type.Should().Be("CONTINUOUS");
        result.Tasks[0].Priority.Should().Be(10);

        // Program assertions
        result.Programs.Should().HaveCount(1);
        result.Programs[0].Name.Should().Be("MainProgram");
        result.Programs[0].LocalTags.Should().HaveCount(1);
        result.Programs[0].LocalTags[0].Name.Should().Be("LocalTag1");
        result.Programs[0].LocalTags[0].DataType.Should().Be("DINT");

        // Routine assertions
        result.Programs[0].Routines.Should().HaveCount(1);
        result.Programs[0].Routines[0].Name.Should().Be("MainRoutine");
        result.Programs[0].Routines[0].Language.Should().Be("RLL");
        result.Programs[0].Routines[0].RungCount.Should().Be(2);

        // Module assertions
        result.IoModules.Should().HaveCount(1);
        result.IoModules[0].Name.Should().Be("Local_IO");
        result.IoModules[0].CatalogNumber.Should().Be("1756-IB16");
    }

    [Fact]
    public async Task ParseAsync_WithInvalidXml_ThrowsException()
    {
        // Arrange
        var invalidXml = "This is not valid XML content";
        var testFile = Path.Combine(_tempDirectory, "invalid.L5X");
        await File.WriteAllTextAsync(testFile, invalidXml);

        // Act
        var action = async () => await _parser.ParseAsync(testFile);

        // Assert
        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Invalid L5X file:*");
    }

    [Fact]
    public async Task ParseAsync_WithEmptyXml_ThrowsException()
    {
        // Arrange
        var emptyXml = "<?xml version='1.0' encoding='UTF-8'?>";
        var testFile = Path.Combine(_tempDirectory, "empty.L5X");
        await File.WriteAllTextAsync(testFile, emptyXml);

        // Act
        var action = async () => await _parser.ParseAsync(testFile);

        // Assert
        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Invalid L5X file:*");
    }

    [Fact]
    public async Task ParseAsync_WithGlobalTags_ParsesTagsCorrectly()
    {
        // Arrange
        var l5xContent = @"<?xml version='1.0' encoding='UTF-8' standalone='yes'?>
<RSLogix5000Content>
  <Controller Name='TestController'>
    <Tags>
      <Tag Name='GlobalTag1' TagType='Base' DataType='BOOL' Usage='Public'>
        <Data>1</Data>
      </Tag>
      <Tag Name='ArrayTag' TagType='Base' DataType='DINT' Dimensions='10' Usage='Public'>
        <Description>Array of integers</Description>
      </Tag>
      <Tag Name='AliasTag' TagType='Alias' AliasFor='GlobalTag1' Usage='Public'/>
    </Tags>
  </Controller>
</RSLogix5000Content>";

        var testFile = Path.Combine(_tempDirectory, "tags.L5X");
        await File.WriteAllTextAsync(testFile, l5xContent);

        // Act
        var result = await _parser.ParseAsync(testFile);

        // Assert
        result.Tags.Should().HaveCount(3);

        var globalTag = result.Tags.FirstOrDefault(t => t.Name == "GlobalTag1");
        globalTag.Should().NotBeNull();
        globalTag!.DataType.Should().Be("BOOL");
        globalTag.Scope.Should().Be("Controller");
        globalTag.Usage.Should().Be("Public");

        var arrayTag = result.Tags.FirstOrDefault(t => t.Name == "ArrayTag");
        arrayTag.Should().NotBeNull();
        arrayTag!.ArrayDimensions.Should().BeEquivalentTo(new[] { 10 });
        arrayTag.Description.Should().Be("Array of integers");

        var aliasTag = result.Tags.FirstOrDefault(t => t.Name == "AliasTag");
        aliasTag.Should().NotBeNull();
        aliasTag!.AliasFor.Should().Be("GlobalTag1");
    }
}