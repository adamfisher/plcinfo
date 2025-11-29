using FluentAssertions;
using PlcInfo.Formatters;
using PlcInfo.Services;
using PlcInfo.Tests.TestHelpers;
using Xunit;
using Xunit.Categories;

namespace PlcInfo.Tests.Formatters;

[UnitTest]
public class TextOutputFormatterTests
{
    private readonly TextOutputFormatter _sut;

    public TextOutputFormatterTests()
    {
        _sut = new TextOutputFormatter();
    }

    [Fact]
    public void FormatSummary_ValidProject_ReturnsFormattedText()
    {
        // Arrange
        var project = TestDataBuilder.CreateSampleProject();

        // Act
        var result = _sut.FormatSummary(project);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("PLC Project Summary");
        result.Should().Contain("test.L5X");
        result.Should().Contain("Rockwell");
        result.Should().Contain("TestController");
        result.Should().Contain("Tasks: 1");
        result.Should().Contain("Programs: 1");
    }

    [Fact]
    public void FormatStructure_ValidProject_ReturnsFormattedStructure()
    {
        // Arrange
        var project = TestDataBuilder.CreateSampleProject();

        // Act
        var result = _sut.FormatStructure(project);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("Project Structure");
        result.Should().Contain("TestController");
        result.Should().Contain("Tasks:");
        result.Should().Contain("MainTask");
        result.Should().Contain("Programs:");
        result.Should().Contain("MainProgram");
    }

    [Fact]
    public void FormatTags_WithoutFilter_ReturnsAllTags()
    {
        // Arrange
        var project = TestDataBuilder.CreateSampleProject();

        // Act
        var result = _sut.FormatTags(project, null);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("Tags");
        result.Should().Contain("TestTag");
        result.Should().Contain("LocalInput");
        result.Should().Contain("Total: 2 tags");
    }

    [Fact]
    public void FormatTags_WithIoOnlyFilter_ReturnsOnlyIoTags()
    {
        // Arrange
        var project = TestDataBuilder.CreateSampleProject();
        var filter = new TagFilter { IoOnly = true };

        // Act
        var result = _sut.FormatTags(project, filter);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("LocalInput");
        result.Should().NotContain("TestTag");
        result.Should().Contain("Total: 1 tags");
    }

    [Fact]
    public void FormatTags_WithScopeFilter_ReturnsMatchingTags()
    {
        // Arrange
        var project = TestDataBuilder.CreateSampleProject();
        var filter = new TagFilter { Scope = "Global" };

        // Act
        var result = _sut.FormatTags(project, filter);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("Total: 2 tags");
    }

    [Fact]
    public void FormatIo_ValidProject_ReturnsFormattedIoConfiguration()
    {
        // Arrange
        var project = TestDataBuilder.CreateSampleProject();

        // Act
        var result = _sut.FormatIo(project);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("I/O Configuration");
        result.Should().Contain("InputModule");
        result.Should().Contain("Digital Input");
        result.Should().Contain("Total Modules: 1");
    }

    [Fact]
    public void FormatMetrics_ValidMetrics_ReturnsFormattedMetrics()
    {
        // Arrange
        var metrics = TestDataBuilder.CreateSampleMetrics();

        // Act
        var result = _sut.FormatMetrics(metrics);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("Project Metrics");
        result.Should().Contain("Total Programs: 5");
        result.Should().Contain("Total Routines: 20");
        result.Should().Contain("Total Tags: 150");
        result.Should().Contain("Complexity Score: 45.5");
        result.Should().Contain("Ladder Logic");
    }

    [Fact]
    public void FormatSearchResults_WithResults_ReturnsFormattedResults()
    {
        // Arrange
        var results = new List<SearchResult>
        {
            new SearchResult
            {
                Type = "Tag",
                Name = "TestTag",
                Location = "MainProgram",
                Context = "Found in program"
            },
            new SearchResult
            {
                Type = "Instruction",
                Name = "TON",
                Location = "MainRoutine",
                Context = "Timer instruction"
            }
        };

        // Act
        var result = _sut.FormatSearchResults(results);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("Search Results");
        result.Should().Contain("Found 2 results");
        result.Should().Contain("TestTag");
        result.Should().Contain("TON");
    }

    [Fact]
    public void FormatSearchResults_EmptyResults_ReturnsNoResultsMessage()
    {
        // Arrange
        var results = new List<SearchResult>();

        // Act
        var result = _sut.FormatSearchResults(results);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("No results found");
    }

    [Fact]
    public void FormatDiff_WithDifferences_ReturnsFormattedDiff()
    {
        // Arrange
        var diffResult = new DiffResult
        {
            File1 = "project1.L5X",
            File2 = "project2.L5X",
            Summary = "Found 3 differences: 1 added, 1 removed, 1 modified",
            HasDifferences = true,
            Details = new List<DiffDetail>
            {
                new DiffDetail
                {
                    Type = "Added",
                    Location = "Tags.NewTag",
                    Message = "Tag 'NewTag' added",
                    Value2 = "Type: DINT, Scope: Global"
                },
                new DiffDetail
                {
                    Type = "Removed",
                    Location = "Tasks.OldTask",
                    Message = "Task 'OldTask' removed",
                    Value1 = "Type: Periodic, Priority: 5"
                },
                new DiffDetail
                {
                    Type = "Modified",
                    Location = "Controller.FirmwareRevision",
                    Message = "Firmware revision changed",
                    Value1 = "31.011",
                    Value2 = "32.011"
                }
            }
        };

        // Act
        var result = _sut.FormatDiff(diffResult);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("Project Diff");
        result.Should().Contain("project1.L5X");
        result.Should().Contain("project2.L5X");
        result.Should().Contain("Added:");
        result.Should().Contain("+ Tags.NewTag");
        result.Should().Contain("Removed:");
        result.Should().Contain("- Tasks.OldTask");
        result.Should().Contain("Modified:");
        result.Should().Contain("* Controller.FirmwareRevision");
        result.Should().Contain("Old: 31.011");
        result.Should().Contain("New: 32.011");
    }

    [Fact]
    public void FormatDiff_NoDifferences_ReturnsNoDifferencesMessage()
    {
        // Arrange
        var diffResult = new DiffResult
        {
            File1 = "project1.L5X",
            File2 = "project2.L5X",
            Summary = "No differences found.",
            HasDifferences = false,
            Details = new List<DiffDetail>()
        };

        // Act
        var result = _sut.FormatDiff(diffResult);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("No differences found");
    }

    [Fact]
    public void FormatDiff_WithInformationalChanges_FormatsCorrectly()
    {
        // Arrange
        var diffResult = new DiffResult
        {
            File1 = "project1.L5X",
            File2 = "project2.L5X",
            Summary = "Found 1 differences: 1 informational",
            HasDifferences = false,
            Details = new List<DiffDetail>
            {
                new DiffDetail
                {
                    Type = "Info",
                    Location = "Project.LastModified",
                    Message = "Last modified date changed",
                    Value1 = "2024-01-01 12:00:00",
                    Value2 = "2024-01-02 14:30:00"
                }
            }
        };

        // Act
        var result = _sut.FormatDiff(diffResult);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("Informational:");
        result.Should().Contain("i Project.LastModified");
    }
}
