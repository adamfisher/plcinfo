using FluentAssertions;
using PlcInfo.Parsing;
using Xunit.Categories;

namespace PlcInfo.Tests.Parsing;

[UnitTest]
public class RockwellAcdParserTests
{
    private readonly RockwellAcdParser _parser;

    public RockwellAcdParserTests()
    {
        _parser = new RockwellAcdParser();
    }

    [Theory]
    [InlineData("test.ACD", true)]
    [InlineData("test.acd", true)]
    [InlineData("test.Acd", true)]
    [InlineData("test.L5X", false)]
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
    public async Task ParseAsync_WithAcdFile_ReturnsBasicProjectInfo()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var acdFile = Path.ChangeExtension(tempFile, ".acd");

        try
        {
            // Create a minimal test file (ACD files are binary, so we'll just create an empty file for basic testing)
            await File.WriteAllBytesAsync(acdFile, new byte[] { 0x50, 0x4B, 0x03, 0x04 }); // ZIP header

            // Act
            var result = await _parser.ParseAsync(acdFile);

            // Assert
            result.Should().NotBeNull();
            result.Vendor.Should().Be("Rockwell Automation");
            result.Format.Should().Be("ACD");
            result.FilePath.Should().Be(acdFile);
            result.Controller.Should().NotBeNull();
            result.Controller!.Vendor.Should().Be("Rockwell Automation");
            result.LastModified.Should().NotBeNull();

            // Since we're not implementing full ACD parsing, expect minimal data
            result.Programs.Should().BeEmpty();
            result.Tags.Should().BeEmpty();
            result.IoModules.Should().BeEmpty();
        }
        finally
        {
            // Cleanup
            if (File.Exists(acdFile))
            {
                File.Delete(acdFile);
            }
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task ParseAsync_WithNonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var nonExistentFile = "C:\\nonexistent\\file.acd";

        // Act
        var action = async () => await _parser.ParseAsync(nonExistentFile);

        // Assert
        await action.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage($"File not found: {nonExistentFile}");
    }
}