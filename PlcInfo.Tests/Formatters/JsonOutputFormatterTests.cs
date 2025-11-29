using System.Text.Json;
using FluentAssertions;
using PlcInfo.Formatters;
using PlcInfo.Services;
using PlcInfo.Tests.TestHelpers;
using Xunit;
using Xunit.Categories;

namespace PlcInfo.Tests.Formatters;

[UnitTest]
public class JsonOutputFormatterTests
{
    private readonly JsonOutputFormatter _sut;

    public JsonOutputFormatterTests()
    {
        _sut = new JsonOutputFormatter();
    }

    [Fact]
    public void FormatSummary_ValidProject_ReturnsValidJson()
    {
        // Arrange
        var project = TestDataBuilder.CreateSampleProject();

        // Act
        var result = _sut.FormatSummary(project);

        // Assert
        result.Should().NotBeNullOrEmpty();
        var jsonDoc = JsonDocument.Parse(result);
        jsonDoc.RootElement.GetProperty("filePath").GetString().Should().Be("test.L5X");
        jsonDoc.RootElement.GetProperty("vendor").GetString().Should().Be("Rockwell");
        jsonDoc.RootElement.GetProperty("statistics").GetProperty("tasks").GetInt32().Should().Be(1);
    }

    [Fact]
    public void FormatStructure_ValidProject_ReturnsValidJson()
    {
        // Arrange
        var project = TestDataBuilder.CreateSampleProject();

        // Act
        var result = _sut.FormatStructure(project);

        // Assert
        result.Should().NotBeNullOrEmpty();
        var jsonDoc = JsonDocument.Parse(result);
        jsonDoc.RootElement.GetProperty("controller").GetProperty("name").GetString()
            .Should().Be("TestController");
        jsonDoc.RootElement.GetProperty("tasks").GetArrayLength().Should().Be(1);
        jsonDoc.RootElement.GetProperty("programs").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void FormatTags_ValidProject_ReturnsValidJson()
    {
        // Arrange
        var project = TestDataBuilder.CreateSampleProject();

        // Act
        var result = _sut.FormatTags(project, null);

        // Assert
        result.Should().NotBeNullOrEmpty();
        var jsonDoc = JsonDocument.Parse(result);
        jsonDoc.RootElement.GetProperty("total").GetInt32().Should().Be(2);
        jsonDoc.RootElement.GetProperty("tags").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void FormatIo_ValidProject_ReturnsValidJson()
    {
        // Arrange
        var project = TestDataBuilder.CreateSampleProject();

        // Act
        var result = _sut.FormatIo(project);

        // Assert
        result.Should().NotBeNullOrEmpty();
        var jsonDoc = JsonDocument.Parse(result);
        jsonDoc.RootElement.GetProperty("modules").GetArrayLength().Should().Be(1);
        jsonDoc.RootElement.GetProperty("ioTagSummary").GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public void FormatMetrics_ValidMetrics_ReturnsValidJson()
    {
        // Arrange
        var metrics = TestDataBuilder.CreateSampleMetrics();

        // Act
        var result = _sut.FormatMetrics(metrics);

        // Assert
        result.Should().NotBeNullOrEmpty();
        var jsonDoc = JsonDocument.Parse(result);
        jsonDoc.RootElement.GetProperty("totalPrograms").GetInt32().Should().Be(5);
        jsonDoc.RootElement.GetProperty("totalTags").GetInt32().Should().Be(150);
        jsonDoc.RootElement.GetProperty("complexityScore").GetDouble().Should().Be(45.5);
    }

    [Fact]
    public void FormatSearchResults_WithResults_ReturnsValidJson()
    {
        // Arrange
        var results = new List<SearchResult>
        {
            new SearchResult { Type = "Tag", Name = "TestTag", Location = "MainProgram", Context = "Test" }
        };

        // Act
        var result = _sut.FormatSearchResults(results);

        // Assert
        result.Should().NotBeNullOrEmpty();
        var jsonDoc = JsonDocument.Parse(result);
        jsonDoc.RootElement.GetProperty("total").GetInt32().Should().Be(1);
        jsonDoc.RootElement.GetProperty("results").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void FormatDiff_WithDifferences_ReturnsValidJson()
    {
        // Arrange
        var diffResult = new DiffResult
        {
            File1 = "project1.L5X",
            File2 = "project2.L5X",
            Summary = "Found differences",
            HasDifferences = true,
            Details = new List<DiffDetail>
            {
                new DiffDetail
                {
                    Type = "Modified",
                    Location = "Test.Property",
                    Message = "Property changed",
                    Value1 = "Old",
                    Value2 = "New"
                }
            }
        };

        // Act
        var result = _sut.FormatDiff(diffResult);

        // Assert
        result.Should().NotBeNullOrEmpty();
        var jsonDoc = JsonDocument.Parse(result);
        jsonDoc.RootElement.GetProperty("file1").GetString().Should().Be("project1.L5X");
        jsonDoc.RootElement.GetProperty("file2").GetString().Should().Be("project2.L5X");
        jsonDoc.RootElement.GetProperty("hasDifferences").GetBoolean().Should().BeTrue();
        jsonDoc.RootElement.GetProperty("details").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void FormatSummary_ProjectWithNullController_HandlesGracefully()
    {
        // Arrange
        var project = TestDataBuilder.CreateSampleProject();
        project = project with { Controller = null };

        // Act
        var result = _sut.FormatSummary(project);

        // Assert
        result.Should().NotBeNullOrEmpty();
        var jsonDoc = JsonDocument.Parse(result);
        jsonDoc.RootElement.TryGetProperty("controller", out var controller).Should().BeTrue();
        controller.ValueKind.Should().Be(JsonValueKind.Null);
    }
}
