using FluentAssertions;
using PlcInfo.Formatters;
using PlcInfo.Models;
using PlcInfo.Services;
using PlcInfo.Tests.Fakers;
using Xunit.Categories;

namespace PlcInfo.Tests.Formatters;

[UnitTest]
public class MarkdownOutputFormatterTests
{
    private readonly MarkdownOutputFormatter _formatter;
    private readonly PlcProjectFaker _projectFaker;
    private readonly MetricsSummaryFaker _metricsFaker;
    private readonly TagInfoFaker _tagFaker;

    public MarkdownOutputFormatterTests()
    {
        _formatter = new MarkdownOutputFormatter();
        _projectFaker = new PlcProjectFaker();
        _metricsFaker = new MetricsSummaryFaker();
        _tagFaker = new TagInfoFaker();
    }

    [Fact]
    public void FormatSummary_WithValidProject_ReturnsMarkdownFormat()
    {
        // Arrange
        var project = _projectFaker.Generate();

        // Act
        var result = _formatter.FormatSummary(project);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("# PLC Project Summary");
        result.Should().Contain("## Project Information");
        result.Should().Contain($"- **Vendor**: {project.Vendor}");
        result.Should().Contain($"- **Format**: {project.Format}");
        result.Should().Contain($"- **File**: `{project.FilePath}`");

        if (project.Controller != null)
        {
            result.Should().Contain("## Controller");
            result.Should().Contain($"- **Name**: {project.Controller.Name}");
        }

        if (project.Programs.Any())
        {
            result.Should().Contain("## Programs");
            result.Should().Contain($"Total Programs: {project.Programs.Count}");
        }
    }

    [Fact]
    public void FormatStructure_WithPrograms_ReturnsMarkdownTree()
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
                        },
                        new()
                        {
                            Name = "Routine2",
                            Type = "Subroutine",
                            Language = "ST",
                            RungCount = 3
                        }
                    }
                }
            }
        };

        // Act
        var result = _formatter.FormatStructure(project);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("# Project Structure");
        result.Should().Contain("## Programs");
        result.Should().Contain("### Program1");
        result.Should().Contain("- **Type**: Program");
        result.Should().Contain("- **Language**: Ladder");
        result.Should().Contain("#### Routines");
        result.Should().Contain("- **Routine1**");
        result.Should().Contain("- **Routine2**");
    }

    [Fact]
    public void FormatTags_WithFilter_ReturnsMarkdownTable()
    {
        // Arrange
        var project = new PlcProject
        {
            Vendor = "Test",
            Format = "Test",
            FilePath = "test.plc",
            Tags = new List<TagInfo>
            {
                new() { Name = "Tag1", DataType = "BOOL", Scope = "Global", Description = "First tag" },
                new() { Name = "Tag2", DataType = "INT", Scope = "Local", Description = "Second tag" },
                new() { Name = "Tag3", DataType = "REAL", Scope = "Global", Address = "DB1.DBD10" }
            }
        };

        // Act
        var result = _formatter.FormatTags(project);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("# Tags");
        result.Should().Contain("| Name | Data Type | Scope | Description | Address |");
        result.Should().Contain("|------|-----------|-------|-------------|---------|");
        result.Should().Contain("| Tag1 | BOOL | Global | First tag | - |");
        result.Should().Contain("| Tag2 | INT | Local | Second tag | - |");
        result.Should().Contain("| Tag3 | REAL | Global | - | DB1.DBD10 |");
    }

    [Fact]
    public void FormatIo_WithIoModules_ReturnsMarkdownSections()
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
                    ChannelCount = 16,
                    Location = "Rack:0, Slot:1"
                },
                new()
                {
                    Name = "Module2",
                    Type = "Analog Output",
                    CatalogNumber = "1756-OF8",
                    Vendor = "Allen-Bradley",
                    ChannelCount = 8,
                    Location = "Rack:0, Slot:2"
                }
            }
        };

        // Act
        var result = _formatter.FormatIo(project);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("# I/O Modules");
        result.Should().Contain("## Module1");
        result.Should().Contain("- **Type**: Digital Input");
        result.Should().Contain("- **Catalog**: 1756-IB16");
        result.Should().Contain("- **Channels**: 16");
        result.Should().Contain("- **Location**: Rack:0, Slot:1");
        result.Should().Contain("## Module2");
    }

    [Fact]
    public void FormatMetrics_WithValidMetrics_ReturnsMarkdownReport()
    {
        // Arrange
        var metrics = new MetricsSummary
        {
            TotalPrograms = 5,
            TotalRoutines = 20,
            TotalTags = 150,
            GlobalTags = 100,
            LocalTags = 50,
            ComplexityScore = 75.5,
            LanguageDistribution = new Dictionary<string, int>
            {
                { "Ladder", 15 },
                { "ST", 5 }
            },
            DataTypeDistribution = new Dictionary<string, int>
            {
                { "BOOL", 80 },
                { "INT", 40 },
                { "REAL", 30 }
            }
        };

        // Act
        var result = _formatter.FormatMetrics(metrics);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("# Project Metrics");
        result.Should().Contain("## Summary");
        result.Should().Contain("- **Total Programs**: 5");
        result.Should().Contain("- **Total Routines**: 20");
        result.Should().Contain("- **Total Tags**: 150");
        result.Should().Contain("- **Complexity Score**: 75.5");
        result.Should().Contain("## Language Distribution");
        result.Should().Contain("| Language | Count |");
        result.Should().Contain("| Ladder | 15 |");
        result.Should().Contain("| ST | 5 |");
        result.Should().Contain("## Data Type Distribution");
        result.Should().Contain("| Data Type | Count |");
        result.Should().Contain("| BOOL | 80 |");
        result.Should().Contain("| INT | 40 |");
        result.Should().Contain("| REAL | 30 |");
    }

    [Fact]
    public void FormatSearchResults_WithResults_ReturnsMarkdownList()
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
            },
            new()
            {
                Type = "Instruction",
                Name = "XIC",
                Location = "Program1/Routine1/Rung 10",
                Context = "XIC(Motor_Start)"
            }
        };

        // Act
        var result = _formatter.FormatSearchResults(results);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("# Search Results");
        result.Should().Contain("Found 3 match(es)");
        result.Should().Contain("## Tag Matches");
        result.Should().Contain("### Motor_Start");
        result.Should().Contain("- **Location**: Global");
        result.Should().Contain("- **Context**: BOOL tag for motor start");
        result.Should().Contain("## Comment Matches");
        result.Should().Contain("## Instruction Matches");
    }

    [Fact]
    public void FormatDiff_WithDifferences_ReturnsMarkdownReport()
    {
        // Arrange
        var diffResult = new DiffResult
        {
            HasDifferences = true,
            Summary = "Found 3 differences between projects",
            Details = new List<DiffDetail>
            {
                new()
                {
                    Type = "Added",
                    Message = "Tag: NewTag - Added tag NewTag (BOOL)"
                },
                new()
                {
                    Type = "Removed",
                    Message = "Program: OldProgram - Removed program OldProgram"
                },
                new()
                {
                    Type = "Modified",
                    Message = "Tag: ModifiedTag - Changed data type from INT to DINT"
                }
            }
        };

        // Act
        var result = _formatter.FormatDiff(diffResult);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("# Diff Results");
        result.Should().Contain("Found 3 differences between projects");
        result.Should().Contain("Added");
        result.Should().Contain("NewTag");
        result.Should().Contain("Removed");
        result.Should().Contain("OldProgram");
        result.Should().Contain("Modified");
        result.Should().Contain("ModifiedTag");
    }

    [Fact]
    public void FormatSummary_WithEmptyProject_ReturnsMinimalMarkdown()
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
        result.Should().Contain("# PLC Project Summary");
        result.Should().Contain("## Project Information");
        result.Should().Contain("- **Vendor**: Test");
        result.Should().Contain("- **Format**: Test");
        result.Should().NotContain("## Controller");
        result.Should().NotContain("## Programs");
        result.Should().NotContain("## Tasks");
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
                    Name = "Tag|With|Pipes",
                    DataType = "STRING",
                    Description = "Description with *asterisks* and _underscores_"
                }
            }
        };

        // Act
        var result = _formatter.FormatTags(project);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("| Tag\\|With\\|Pipes |");
        // Markdown should properly escape special characters
    }
}