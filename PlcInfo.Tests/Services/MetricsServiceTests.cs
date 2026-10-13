using FluentAssertions;
using PlcInfo.Models;
using PlcInfo.Services;
using PlcInfo.Tests.Fakers;
using Xunit.Categories;

namespace PlcInfo.Tests.Services;

[UnitTest]
public class MetricsServiceTests
{
    private readonly MetricsService _service;
    private readonly PlcProjectFaker _projectFaker;
    private readonly ProgramInfoFaker _programFaker;
    private readonly TagInfoFaker _tagFaker;
    private readonly IoModuleInfoFaker _moduleFaker;
    private readonly BlockInfoFaker _blockFaker;
    private readonly TaskInfoFaker _taskFaker;

    public MetricsServiceTests()
    {
        _service = new MetricsService();
        _projectFaker = new PlcProjectFaker();
        _programFaker = new ProgramInfoFaker();
        _tagFaker = new TagInfoFaker();
        _moduleFaker = new IoModuleInfoFaker();
        _blockFaker = new BlockInfoFaker();
        _taskFaker = new TaskInfoFaker();
    }

    [Fact]
    public void CalculateMetrics_WithEmptyProject_ReturnsZeroMetrics()
    {
        // Arrange
        var project = new PlcProject
        {
            Vendor = "Test",
            Format = "Test",
            FilePath = "test.plc"
        };

        // Act
        var result = _service.CalculateMetrics(project);

        // Assert
        result.Should().NotBeNull();
        result.TotalPrograms.Should().Be(0);
        result.TotalRoutines.Should().Be(0);
        result.TotalRungs.Should().Be(0);
        result.TotalBlocks.Should().Be(0);
        result.TotalNetworks.Should().Be(0);
        result.TotalTags.Should().Be(0);
        result.GlobalTags.Should().Be(0);
        result.LocalTags.Should().Be(0);
        result.IoTags.Should().Be(0);
        result.TotalIoModules.Should().Be(0);
        result.InputModules.Should().Be(0);
        result.OutputModules.Should().Be(0);
        result.TotalTasks.Should().Be(0);
        result.TotalInstructions.Should().Be(0);
        result.ComplexityScore.Should().BeGreaterThanOrEqualTo(0).And.BeLessThanOrEqualTo(100);
    }

    [Fact]
    public void CalculateMetrics_WithPrograms_CountsProgramMetricsCorrectly()
    {
        // Arrange
        var programs = _programFaker.Generate(5);
        var project = new PlcProject
        {
            Vendor = "Test",
            Format = "Test",
            FilePath = "test.plc",
            Programs = programs
        };

        // Act
        var result = _service.CalculateMetrics(project);

        // Assert
        result.TotalPrograms.Should().Be(5);
        result.TotalRoutines.Should().Be(programs.Sum(p => p.Routines.Count));
        result.TotalRungs.Should().Be(programs.SelectMany(p => p.Routines).Sum(r => r.RungCount));
    }

    [Fact]
    public void CalculateMetrics_WithTags_CountsTagMetricsCorrectly()
    {
        // Arrange
        var tags = new List<TagInfo>();

        // Add global tags
        for (int i = 0; i < 10; i++)
        {
            var tag = _tagFaker.Generate();
            tag.Scope = "Global";
            tag.IsIoTag = false;
            tags.Add(tag);
        }

        // Add controller tags
        for (int i = 0; i < 5; i++)
        {
            var tag = _tagFaker.Generate();
            tag.Scope = "Controller";
            tag.IsIoTag = false;
            tags.Add(tag);
        }

        // Add local tags
        for (int i = 0; i < 8; i++)
        {
            var tag = _tagFaker.Generate();
            tag.Scope = "Local";
            tag.IsIoTag = false;
            tags.Add(tag);
        }

        // Add IO tags
        for (int i = 0; i < 12; i++)
        {
            var tag = _tagFaker.Generate();
            tag.Scope = "Global";
            tag.IsIoTag = true;
            tags.Add(tag);
        }

        var project = new PlcProject
        {
            Vendor = "Test",
            Format = "Test",
            FilePath = "test.plc",
            Tags = tags
        };

        // Act
        var result = _service.CalculateMetrics(project);

        // Assert
        result.TotalTags.Should().Be(35);
        result.GlobalTags.Should().Be(27); // 10 global + 5 controller + 12 IO tags (which are also global)
        result.LocalTags.Should().Be(8);
        result.IoTags.Should().Be(12);
    }

    [Fact]
    public void CalculateMetrics_WithIoModules_CountsModulesCorrectly()
    {
        // Arrange
        var modules = new List<IoModuleInfo>();

        // Add input modules
        for (int i = 0; i < 6; i++)
        {
            var module = _moduleFaker.Generate();
            module.Type = "Digital Input";
            modules.Add(module);
        }

        // Add output modules
        for (int i = 0; i < 4; i++)
        {
            var module = _moduleFaker.Generate();
            module.Type = "Digital Output";
            modules.Add(module);
        }

        // Add mixed type modules
        for (int i = 0; i < 2; i++)
        {
            var module = _moduleFaker.Generate();
            module.Type = "Communication Module";
            modules.Add(module);
        }

        var project = new PlcProject
        {
            Vendor = "Test",
            Format = "Test",
            FilePath = "test.plc",
            IoModules = modules
        };

        // Act
        var result = _service.CalculateMetrics(project);

        // Assert
        result.TotalIoModules.Should().Be(12);
        result.InputModules.Should().Be(6);
        result.OutputModules.Should().Be(4);
    }

    [Fact]
    public void CalculateMetrics_WithBlocks_CountsBlockMetricsCorrectly()
    {
        // Arrange
        var blocks = _blockFaker.Generate(10);
        var totalNetworks = blocks.Sum(b => b.NetworkCount);

        var project = new PlcProject
        {
            Vendor = "Test",
            Format = "Test",
            FilePath = "test.plc",
            Blocks = blocks
        };

        // Act
        var result = _service.CalculateMetrics(project);

        // Assert
        result.TotalBlocks.Should().Be(10);
        result.TotalNetworks.Should().Be(totalNetworks);
    }

    [Fact]
    public void CalculateMetrics_WithTasks_CountsTasksCorrectly()
    {
        // Arrange
        var tasks = _taskFaker.Generate(5);

        var project = new PlcProject
        {
            Vendor = "Test",
            Format = "Test",
            FilePath = "test.plc",
            Tasks = tasks
        };

        // Act
        var result = _service.CalculateMetrics(project);

        // Assert
        result.TotalTasks.Should().Be(5);
    }

    [Fact]
    public void CalculateMetrics_WithFullProject_CalculatesAllMetrics()
    {
        // Arrange
        var project = _projectFaker.Generate();

        // Act
        var result = _service.CalculateMetrics(project);

        // Assert
        result.Should().NotBeNull();
        result.TotalPrograms.Should().Be(project.Programs.Count);
        result.TotalRoutines.Should().Be(project.Programs.Sum(p => p.Routines.Count));
        result.TotalBlocks.Should().Be(project.Blocks.Count);
        result.TotalTags.Should().Be(project.Tags.Count + project.Programs.Sum(p => p.LocalTags.Count));
        result.TotalIoModules.Should().Be(project.IoModules.Count);
        result.TotalTasks.Should().Be(project.Tasks.Count);
        result.ComplexityScore.Should().BeInRange(0, 100);

        // Language distribution should have entries if there are programs or blocks
        if (project.Programs.Any() || project.Blocks.Any())
        {
            result.LanguageDistribution.Should().NotBeEmpty();
        }

        // Data type distribution should have entries if there are tags
        if (project.Tags.Any())
        {
            result.DataTypeDistribution.Should().NotBeEmpty();
        }
    }

    [Fact]
    public void CalculateMetrics_WithMixedCaseScopeNames_HandlesCorrectly()
    {
        // Arrange
        var tags = new List<TagInfo>
        {
            new() { Name = "Tag1", DataType = "INT", Scope = "GLOBAL" },
            new() { Name = "Tag2", DataType = "BOOL", Scope = "global" },
            new() { Name = "Tag3", DataType = "REAL", Scope = "Global" },
            new() { Name = "Tag4", DataType = "INT", Scope = "CONTROLLER" },
            new() { Name = "Tag5", DataType = "BOOL", Scope = "controller" },
            new() { Name = "Tag6", DataType = "INT", Scope = "LOCAL" },
            new() { Name = "Tag7", DataType = "BOOL", Scope = "local" },
            new() { Name = "Tag8", DataType = "INT", Scope = "Program" }
        };

        var project = new PlcProject
        {
            Vendor = "Test",
            Format = "Test",
            FilePath = "test.plc",
            Tags = tags
        };

        // Act
        var result = _service.CalculateMetrics(project);

        // Assert
        result.GlobalTags.Should().Be(5); // 3 global + 2 controller
        result.LocalTags.Should().Be(3); // 2 local + 1 program
    }

    [Fact]
    public void CalculateMetrics_ComplexityScore_StaysWithinBounds()
    {
        // Arrange & Act
        for (int i = 0; i < 10; i++)
        {
            var project = _projectFaker.Generate();
            var result = _service.CalculateMetrics(project);

            // Assert
            result.ComplexityScore.Should().BeInRange(0, 100);
        }
    }
}