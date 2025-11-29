using System.IO.Compression;
using FluentAssertions;
using PlcInfo.Parsing;
using Xunit.Categories;

namespace PlcInfo.Tests.Parsing;

[UnitTest]
public class SiemensTiaPortalParserTests
{
    private readonly SiemensTiaPortalParser _parser;

    public SiemensTiaPortalParserTests()
    {
        _parser = new SiemensTiaPortalParser();
    }

    [Theory]
    [InlineData("test.zap13", true)]
    [InlineData("test.zap14", true)]
    [InlineData("test.zap15", true)]
    [InlineData("test.zap16", true)]
    [InlineData("test.zap17", true)]
    [InlineData("test.ap15_1", true)]
    [InlineData("test.ap16", true)]
    [InlineData("test.ap17", true)]
    [InlineData("test.ap18", true)]
    [InlineData("test.ZAP15", true)]
    [InlineData("test.AP16", true)]
    [InlineData("test.zip", false)]
    [InlineData("test.xml", false)]
    [InlineData("test.s7p", false)]
    public void CanParse_WithVariousExtensions_ReturnsExpectedResult(string filename, bool expected)
    {
        // Act
        var result = _parser.CanParse(filename);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public async Task ParseAsync_WithTiaPortalFile_ReturnsBasicProjectInfo()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var tiaFile = Path.ChangeExtension(tempFile, ".ap17");

        try
        {
            // Create a minimal valid ZIP archive
            CreateMinimalZipFile(tiaFile);

            // Act
            var result = await _parser.ParseAsync(tiaFile);

            // Assert
            result.Should().NotBeNull();
            result.Vendor.Should().Be("Siemens");
            result.Format.Should().Be("TIA Portal V17");
            result.FilePath.Should().Be(tiaFile);

            // With a minimal ZIP file, we may not have controller info
            // Just verify the basic parsing succeeded

            result.LastModified.Should().NotBeNull();

            // Since we're not implementing full TIA Portal parsing, expect minimal data
            result.Blocks.Should().BeEmpty();
            result.Tags.Should().BeEmpty();
            result.IoModules.Should().BeEmpty();
        }
        finally
        {
            // Cleanup
            if (File.Exists(tiaFile))
            {
                File.Delete(tiaFile);
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
        var nonExistentFile = "C:\\nonexistent\\project.ap17";

        // Act
        var action = async () => await _parser.ParseAsync(nonExistentFile);

        // Assert
        await action.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage($"File not found: {nonExistentFile}");
    }

    [Theory]
    [InlineData(".zap13", "TIA Portal V13")]
    [InlineData(".zap14", "TIA Portal V14")]
    [InlineData(".zap15", "TIA Portal V15")]
    [InlineData(".zap16", "TIA Portal V16")]
    [InlineData(".zap17", "TIA Portal V17")]
    [InlineData(".ap15_1", "TIA Portal V15.1")]
    [InlineData(".ap16", "TIA Portal V16")]
    [InlineData(".ap17", "TIA Portal V17")]
    [InlineData(".ap18", "TIA Portal V18")]
    public async Task ParseAsync_WithDifferentVersions_ReturnsCorrectFormat(string extension, string expectedFormat)
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var tiaFile = Path.ChangeExtension(tempFile, extension);

        try
        {
            // Create a minimal valid ZIP archive
            CreateMinimalZipFile(tiaFile);

            // Act
            var result = await _parser.ParseAsync(tiaFile);

            // Assert
            result.Format.Should().Be(expectedFormat);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tiaFile))
            {
                File.Delete(tiaFile);
            }
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private static void CreateMinimalZipFile(string filePath)
    {
        // Create a minimal valid ZIP archive with an empty dummy file
        using (var archive = ZipFile.Open(filePath, ZipArchiveMode.Create))
        {
            // Add an empty entry to make the ZIP valid
            var entry = archive.CreateEntry("dummy.txt");
            using (var stream = entry.Open())
            {
                // Write nothing - just create an empty file
            }
        }
    }
}