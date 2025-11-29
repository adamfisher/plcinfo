using FluentAssertions;
using PlcInfo.Parsing;
using Xunit.Categories;

namespace PlcInfo.Tests.Parsing;

[UnitTest]
public class SiemensStep7ParserTests
{
    private readonly SiemensStep7Parser _parser;

    public SiemensStep7ParserTests()
    {
        _parser = new SiemensStep7Parser();
    }

    [Theory]
    [InlineData("test.s7p", true)]
    [InlineData("test.S7P", true)]
    [InlineData("test.s7P", true)]
    [InlineData("test.zip", false)]
    [InlineData("test.xml", false)]
    [InlineData("test.L5X", false)]
    public void CanParse_WithVariousExtensions_ReturnsExpectedResult(string filename, bool expected)
    {
        // Act
        var result = _parser.CanParse(filename);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public async Task ParseAsync_WithS7pFile_ReturnsBasicProjectInfo()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var s7pFile = Path.ChangeExtension(tempFile, ".s7p");

        try
        {
            // Create a minimal test file (S7P files are archives, so we'll create a simple file for testing)
            await File.WriteAllBytesAsync(s7pFile, new byte[] { 0x50, 0x4B, 0x03, 0x04 }); // ZIP header

            // Act
            var result = await _parser.ParseAsync(s7pFile);

            // Assert
            result.Should().NotBeNull();
            result.Vendor.Should().Be("Siemens");
            result.Format.Should().Be("Step 7 Classic");
            result.FilePath.Should().Be(s7pFile);

            result.Controller.Should().NotBeNull();
            result.Controller!.Vendor.Should().Be("Siemens");
            result.Controller.Name.Should().Be("S7 Controller");
            result.Controller.Type.Should().Be("S7-300/400");

            result.LastModified.Should().NotBeNull();

            // Since we're not implementing full S7P parsing, expect minimal data
            result.Blocks.Should().BeEmpty();
            result.Tags.Should().BeEmpty();
            result.IoModules.Should().BeEmpty();
        }
        finally
        {
            // Cleanup
            if (File.Exists(s7pFile))
            {
                File.Delete(s7pFile);
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
        var nonExistentFile = "C:\\nonexistent\\project.s7p";

        // Act
        var action = async () => await _parser.ParseAsync(nonExistentFile);

        // Assert
        await action.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage($"File not found: {nonExistentFile}");
    }

    [Fact]
    public void GetSupportedFormats_ReturnsS7pExtension()
    {
        // Act
        var extensions = new[] { ".s7p", ".S7P", ".s7P" };

        // Assert
        foreach (var ext in extensions)
        {
            var file = $"test{ext}";
            _parser.CanParse(file).Should().BeTrue($"Should support {ext} extension");
        }
    }
}