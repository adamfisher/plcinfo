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
    [InlineData("test.awl", true)]
    [InlineData("TEST.AWL", true)]
    [InlineData("test.scl", true)]
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

    [Fact]
    public async Task ParseAsync_AwlWithDataBlockAndCodeBlock_ParsesBoth()
    {
        var path = Path.Combine(Path.GetTempPath(), $"step7_{Guid.NewGuid():N}.awl");
        await File.WriteAllTextAsync(path, @"DATA_BLOCK DB 5
TITLE =Recipe
  STRUCT
   Speed : REAL := 1.5;	//Set speed
   Axis : STRUCT
    Pos : DINT ;
   END_STRUCT ;
  END_STRUCT ;
BEGIN
   Speed := 2.0;
END_DATA_BLOCK

FUNCTION FC 3 : VOID
VAR_INPUT
  In1 : BOOL ;
END_VAR
BEGIN
NETWORK
TITLE =First
//Explain
M001: A     #In1; // check
      =     Q      4.0;
END_FUNCTION
");
        try
        {
            var project = await _parser.ParseAsync(path);

            project.Format.Should().Be("Step 7 AWL source");
            project.Blocks.Select(b => (b.Name, b.Type, b.Number)).Should().Equal(("DB5", "DB", 5), ("FC3", "FC", 3));
            project.Tags.Select(t => (t.Name, t.DataType, t.Scope, t.InitialValue)).Should().Equal(
                ("Speed", "REAL", "DB5", "1.5"), ("Axis", "Struct", "DB5", null), ("Axis.Pos", "DINT", "DB5", null));
            project.Tags[0].Description.Should().Be("Set speed");

            var fc = project.Blocks[1];
            fc.Properties["ReturnType"].Should().Be("VOID");
            fc.Interface.Should().Equal(new Dictionary<string, string> { ["Input.In1"] = "BOOL" });
            var network = fc.Networks.Should().ContainSingle().Subject;
            (network.Title, network.Comment).Should().Be(("First", "Explain"));
            network.Text.Should().Be("M001: A #In1 //check\n= Q 4.0");
            network.Instructions.Select(i => (i.Mnemonic, i.Operands.Single())).Should().Equal(("A", "#In1"), ("=", "Q 4.0"));
            network.Instructions[0].Properties["Label"].Should().Be("M001");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ParseAsync_SclWithTypeAndUnquotedName_ParsesDataTypeAndBlock()
    {
        var path = Path.Combine(Path.GetTempPath(), $"step7_{Guid.NewGuid():N}.scl");
        await File.WriteAllTextAsync(path, @"TYPE ""udt_Row""
VERSION : 0.1
   STRUCT
      Count : Int;
      Items : Array[0..4] of ""udt_Item"";
   END_STRUCT;

END_TYPE

FUNCTION Str_Clear : Void
VAR_IN_OUT
    Str : STRING;
END_VAR

    Str := '';

END_FUNCTION
");
        try
        {
            var project = await _parser.ParseAsync(path);

            var type = project.DataTypes.Should().ContainSingle().Subject;
            type.Name.Should().Be("udt_Row");
            type.Members.Select(m => (m.Name, m.DataType)).Should().Equal(("Count", "Int"), ("Items", "Array[0..4] of \"udt_Item\""));

            var block = project.Blocks.Should().ContainSingle().Subject;
            (block.Name, block.Type, block.Language).Should().Be(("Str_Clear", "FC", "SCL"));
            block.Networks.Should().ContainSingle().Which.Text.Should().Be("    Str := '';");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ParseAsync_SourceWithoutBlocks_Throws()
    {
        var path = Path.Combine(Path.GetTempPath(), $"step7_{Guid.NewGuid():N}.awl");
        await File.WriteAllTextAsync(path, "just some text\n");
        try
        {
            var action = async () => await _parser.ParseAsync(path);

            await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*contains no ORGANIZATION_BLOCK*");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
