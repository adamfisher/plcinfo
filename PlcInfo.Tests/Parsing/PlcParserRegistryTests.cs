using FluentAssertions;
using Moq;
using PlcInfo.Models;
using PlcInfo.Parsing;
using Xunit.Categories;

namespace PlcInfo.Tests.Parsing;

[UnitTest]
public class PlcParserRegistryTests
{
    [Fact]
    public void ResolveForFile_WithMatchingParser_ReturnsCorrectParser()
    {
        // Arrange
        var mockParser1 = new Mock<IPlcProjectParser>();
        mockParser1.Setup(x => x.CanParse(It.IsAny<string>())).Returns(false);

        var mockParser2 = new Mock<IPlcProjectParser>();
        mockParser2.Setup(x => x.CanParse("test.L5X")).Returns(true);
        mockParser2.Setup(x => x.CanParse(It.Is<string>(s => s != "test.L5X"))).Returns(false);

        var registry = new PlcParserRegistry(new[] { mockParser1.Object, mockParser2.Object });

        // Act
        var result = registry.ResolveForFile("test.L5X");

        // Assert
        result.Should().Be(mockParser2.Object);
    }

    [Fact]
    public void ResolveForFile_WithNoMatchingParser_ThrowsNotSupportedException()
    {
        // Arrange
        var mockParser = new Mock<IPlcProjectParser>();
        mockParser.Setup(x => x.CanParse(It.IsAny<string>())).Returns(false);

        var registry = new PlcParserRegistry(new[] { mockParser.Object });

        // Act & Assert
        var action = () => registry.ResolveForFile("unknown.xyz");
        action.Should().Throw<NotSupportedException>()
            .WithMessage("No parser found for file: unknown.xyz");
    }

    [Fact]
    public void ResolveForFile_WithMultipleMatchingParsers_ReturnsFirstMatch()
    {
        // Arrange
        var mockParser1 = new Mock<IPlcProjectParser>();
        mockParser1.Setup(x => x.CanParse("test.L5X")).Returns(true);

        var mockParser2 = new Mock<IPlcProjectParser>();
        mockParser2.Setup(x => x.CanParse("test.L5X")).Returns(true);

        var registry = new PlcParserRegistry(new[] { mockParser1.Object, mockParser2.Object });

        // Act
        var result = registry.ResolveForFile("test.L5X");

        // Assert
        result.Should().Be(mockParser1.Object);
    }

    [Fact]
    public void GetAllParsers_ReturnsAllRegisteredParsers()
    {
        // Arrange
        var mockParser1 = new Mock<IPlcProjectParser>();
        var mockParser2 = new Mock<IPlcProjectParser>();
        var mockParser3 = new Mock<IPlcProjectParser>();

        var parsers = new[] { mockParser1.Object, mockParser2.Object, mockParser3.Object };
        var registry = new PlcParserRegistry(parsers);

        // Act
        var result = registry.GetAllParsers();

        // Assert
        result.Should().HaveCount(3);
        result.Should().Contain(parsers);
    }

    [Fact]
    public void Constructor_WithEmptyParserList_CreatesEmptyRegistry()
    {
        // Arrange & Act
        var registry = new PlcParserRegistry(Array.Empty<IPlcProjectParser>());

        // Assert
        registry.GetAllParsers().Should().BeEmpty();
    }

    [Fact]
    public void ResolveForFile_WithCaseInsensitiveExtension_WorksCorrectly()
    {
        // Arrange
        var mockParser = new Mock<IPlcProjectParser>();
        mockParser.Setup(x => x.CanParse(It.IsAny<string>()))
            .Returns<string>(path => path.EndsWith(".L5X", StringComparison.OrdinalIgnoreCase));

        var registry = new PlcParserRegistry(new[] { mockParser.Object });

        // Act
        var result1 = registry.ResolveForFile("test.L5X");
        var result2 = registry.ResolveForFile("test.l5x");
        var result3 = registry.ResolveForFile("test.L5x");

        // Assert
        result1.Should().Be(mockParser.Object);
        result2.Should().Be(mockParser.Object);
        result3.Should().Be(mockParser.Object);
    }
}