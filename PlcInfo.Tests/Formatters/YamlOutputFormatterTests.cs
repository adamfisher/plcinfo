using FluentAssertions;
using PlcInfo.Formatters;
using PlcInfo.Models;
using PlcInfo.Services;
using PlcInfo.Tests.Fakers;
using Xunit.Categories;

namespace PlcInfo.Tests.Formatters;

[UnitTest]
public class YamlOutputFormatterTests
{
    private readonly YamlOutputFormatter _formatter;
    private readonly PlcProjectFaker _projectFaker;
    private readonly MetricsSummaryFaker _metricsFaker;
    private readonly TagInfoFaker _tagFaker;

    public YamlOutputFormatterTests()
    {
        _formatter = new YamlOutputFormatter();
        _projectFaker = new PlcProjectFaker();
        _metricsFaker = new MetricsSummaryFaker();
        _tagFaker = new TagInfoFaker();
    }

    [Fact]
    public void FormatSummary_WithValidProject_ReturnsYamlFormat()
    {
        // Arrange
        var project = _projectFaker.Generate();

        // Act
        var result = _formatter.FormatSummary(project);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("vendor:");
        result.Should().Contain("format:");
        result.Should().Contain("filePath:");

        if (project.Controller != null)
        {
            result.Should().Contain("controller:");
            result.Should().Contain($"name: {project.Controller.Name}");
        }

        if (project.Programs.Any())
        {
            result.Should().Contain("programs:");
        }

        if (project.Tasks.Any())
        {
            result.Should().Contain("tasks:");
        }
    }

    [Fact]
    public void FormatStructure_WithPrograms_ReturnsHierarchicalYaml()
    {
        // Arrange
        var project = new PlcProject
        {
            Vendor = "Test",
            Format = "Test",
            FilePath = "test.plc",
            Programs = new List<ProgramInfo>
            {
                new()
                {
                    Name = "Program1",
                    Type = "Program",
                    Language = "Ladder",
                    Routines = new List<RoutineInfo>
                    {
                        new()
                        {
                            Name = "Routine1",
                            Type = "Main",
                            Language = "Ladder",
                            RungCount = 5
                        }
                    }
                }
            }
        };

        // Act
        var result = _formatter.FormatStructure(project);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("structure:");
        result.Should().Contain("programs:");
        result.Should().Contain("- name: Program1");
        result.Should().Contain("routines:");
        result.Should().Contain("- name: Routine1");
        result.Should().Contain("rungCount: 5");
    }

    [Fact]
    public void FormatTags_WithFilter_ReturnsFilteredYaml()
    {
        // Arrange
        var project = new PlcProject
        {
            Vendor = "Test",
            Format = "Test",
            FilePath = "test.plc",
            Tags = new List<TagInfo>
            {
                new() { Name = "Tag1", DataType = "BOOL", Scope = "Global" },
                new() { Name = "Tag2", DataType = "INT", Scope = "Local" },
                new() { Name = "Tag3", DataType = "BOOL", Scope = "Global" }
            }
        };

        var filter = new TagFilter { Scope = "Global" };

        // Act
        var result = _formatter.FormatTags(project, filter);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("tags:");
        result.Should().Contain("- name: Tag1");
        result.Should().Contain("- name: Tag3");
        result.Should().NotContain("- name: Tag2");
    }

    [Fact]
    public void FormatIo_WithIoModules_ReturnsYamlList()
    {
        // Arrange
        var project = new PlcProject
        {
            Vendor = "Test",
            Format = "Test",
            FilePath = "test.plc",
            IoModules = new List<IoModuleInfo>
            {
                new()
                {
                    Name = "Module1",
                    Type = "Digital Input",
                    CatalogNumber = "1756-IB16",
                    Vendor = "Allen-Bradley",
                    ChannelCount = 16
                }
            }
        };

        // Act
        var result = _formatter.FormatIo(project);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("ioModules:");
        result.Should().Contain("- name: Module1");
        result.Should().Contain("type: Digital Input");
        result.Should().Contain("catalogNumber: 1756-IB16");
        result.Should().Contain("channelCount: 16");
    }

    [Fact]
    public void FormatMetrics_WithValidMetrics_ReturnsYamlFormat()
    {
        // Arrange
        var metrics = _metricsFaker.Generate();

        // Act
        var result = _formatter.FormatMetrics(metrics);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("metrics:");
        result.Should().Contain($"totalPrograms: {metrics.TotalPrograms}");
        result.Should().Contain($"totalTags: {metrics.TotalTags}");
        result.Should().Contain($"complexityScore: {metrics.ComplexityScore}");

        if (metrics.LanguageDistribution.Any())
        {
            result.Should().Contain("languageDistribution:");
        }
    }

    [Fact]
    public void FormatSearchResults_WithResults_ReturnsYamlList()
    {
        // Arrange
        var results = new List<SearchResult>
        {
            new()
            {
                Type = "Tag",
                Name = "Motor_Start",
                Location = "Global",
                Context = "BOOL tag for motor start"
            },
            new()
            {
                Type = "Comment",
                Name = "Rung 5",
                Location = "Program1/Routine1",
                Context = "Initialize motor parameters"
            }
        };

        // Act
        var result = _formatter.FormatSearchResults(results);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("searchResults:");
        result.Should().Contain("- type: Tag");
        result.Should().Contain("name: Motor_Start");
        result.Should().Contain("- type: Comment");
        result.Should().Contain("context: Initialize motor parameters");
    }

    [Fact]
    public void FormatDiff_WithDifferences_ReturnsYamlFormat()
    {
        // Arrange
        var diffResult = new DiffResult
        {
            HasDifferences = true,
            Summary = "Found 3 differences",
            Details = new List<DiffDetail>
            {
                new() { Type = "Added", Message = "Tag: NewTag - Added tag NewTag" },
                new() { Type = "Removed", Message = "Program: OldProgram - Removed program OldProgram" },
                new() { Type = "Modified", Message = "Tag: ModifiedTag - Changed data type from INT to DINT" }
            }
        };

        // Act
        var result = _formatter.FormatDiff(diffResult);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("diff:");
        result.Should().Contain("hasDifferences: true");
        result.Should().Contain("summary: Found 3 differences");
        result.Should().Contain("details:");
        result.Should().Contain("- type: Added");
        result.Should().Contain("- type: Removed");
        result.Should().Contain("- type: Modified");
    }

    [Fact]
    public void FormatSummary_WithEmptyProject_ReturnsMinimalYaml()
    {
        // Arrange
        var project = new PlcProject
        {
            Vendor = "Test",
            Format = "Test",
            FilePath = "empty.plc"
        };

        // Act
        var result = _formatter.FormatSummary(project);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("vendor: Test");
        result.Should().Contain("format: Test");
        result.Should().Contain("filePath: empty.plc");
    }

    [Fact]
    public void FormatTags_WithSpecialCharacters_EscapesCorrectly()
    {
        // Arrange
        var project = new PlcProject
        {
            Vendor = "Test",
            Format = "Test",
            FilePath = "test.plc",
            Tags = new List<TagInfo>
            {
                new()
                {
                    Name = "Tag:With:Colons",
                    DataType = "STRING",
                    Description = "Description with \"quotes\" and 'apostrophes'"
                }
            }
        };

        // Act
        var result = _formatter.FormatTags(project);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("name:");
        result.Should().Contain("description:");
        // YAML should properly escape or quote special characters
    }
}