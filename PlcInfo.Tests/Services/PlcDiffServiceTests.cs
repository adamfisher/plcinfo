using FluentAssertions;
using PlcInfo.Models;
using PlcInfo.Services;
using PlcInfo.Tests.TestHelpers;
using Xunit;
using Xunit.Categories;

namespace PlcInfo.Tests.Services;

[UnitTest]
public class PlcDiffServiceTests
{
    private readonly PlcDiffService _sut;

    public PlcDiffServiceTests()
    {
        _sut = new PlcDiffService();
    }

    [Fact]
    public void Compare_IdenticalProjects_ReturnsNoDifferences()
    {
        // Arrange
        var project1 = TestDataBuilder.CreateSampleProject("project1.L5X");
        var project2 = TestDataBuilder.CreateSampleProject("project1.L5X");

        // Act
        var result = _sut.Compare(project1, project2);

        // Assert
        result.Should().NotBeNull();
        result.HasDifferences.Should().BeFalse();
        result.Summary.Should().Contain("No differences found");
        result.Details.Where(d => d.Type != "Info").Should().BeEmpty();
    }

    [Fact]
    public void Compare_DifferentVendors_DetectsDifference()
    {
        // Arrange
        var project1 = TestDataBuilder.CreateSampleProject("project1.L5X", "Rockwell");
        var project2 = TestDataBuilder.CreateSampleProject("project2.L5X", "Siemens");

        // Act
        var result = _sut.Compare(project1, project2);

        // Assert
        result.HasDifferences.Should().BeTrue();
        result.Details.Should().Contain(d =>
            d.Type == "Modified" &&
            d.Location == "Project.Vendor" &&
            d.Value1 == "Rockwell" &&
            d.Value2 == "Siemens");
    }

    [Fact]
    public void Compare_DifferentControllerName_DetectsDifference()
    {
        // Arrange
        var project1 = TestDataBuilder.CreateSampleProject();
        var project2 = TestDataBuilder.CreateSampleProject();
        project2 = project2 with
        {
            Controller = TestDataBuilder.CreateSampleController("NewController")
        };

        // Act
        var result = _sut.Compare(project1, project2);

        // Assert
        result.HasDifferences.Should().BeTrue();
        result.Details.Should().Contain(d =>
            d.Type == "Modified" &&
            d.Location == "Controller.Name" &&
            d.Value1 == "TestController" &&
            d.Value2 == "NewController");
    }

    [Fact]
    public void Compare_AddedTask_DetectsAddition()
    {
        // Arrange
        var project1 = TestDataBuilder.CreateSampleProject();
        var project2 = TestDataBuilder.CreateSampleProject();
        var newTask = TestDataBuilder.CreateSampleTask("NewTask", 20);
        project2 = project2 with
        {
            Tasks = new List<TaskInfo> { project2.Tasks[0], newTask }
        };

        // Act
        var result = _sut.Compare(project1, project2);

        // Assert
        result.HasDifferences.Should().BeTrue();
        result.Details.Should().Contain(d =>
            d.Type == "Added" &&
            d.Location == "Tasks.NewTask" &&
            d.Message.Contains("NewTask"));
    }

    [Fact]
    public void Compare_RemovedTask_DetectsRemoval()
    {
        // Arrange
        var project1 = TestDataBuilder.CreateSampleProject();
        var project2 = TestDataBuilder.CreateSampleProject();
        project2 = project2 with { Tasks = new List<TaskInfo>() };

        // Act
        var result = _sut.Compare(project1, project2);

        // Assert
        result.HasDifferences.Should().BeTrue();
        result.Details.Should().Contain(d =>
            d.Type == "Removed" &&
            d.Location == "Tasks.MainTask");
    }

    [Fact]
    public void Compare_ModifiedTaskPriority_DetectsModification()
    {
        // Arrange
        var project1 = TestDataBuilder.CreateSampleProject();
        var project2 = TestDataBuilder.CreateSampleProject();
        var modifiedTask = TestDataBuilder.CreateSampleTask("MainTask", 15);
        project2 = project2 with { Tasks = new List<TaskInfo> { modifiedTask } };

        // Act
        var result = _sut.Compare(project1, project2);

        // Assert
        result.HasDifferences.Should().BeTrue();
        result.Details.Should().Contain(d =>
            d.Type == "Modified" &&
            d.Location == "Tasks.MainTask.Priority" &&
            d.Value1 == "10" &&
            d.Value2 == "15");
    }

    [Fact]
    public void Compare_AddedProgram_DetectsAddition()
    {
        // Arrange
        var project1 = TestDataBuilder.CreateSampleProject();
        var project2 = TestDataBuilder.CreateSampleProject();
        var newProgram = TestDataBuilder.CreateSampleProgram("SecondProgram");
        project2 = project2 with
        {
            Programs = new List<ProgramInfo> { project2.Programs[0], newProgram }
        };

        // Act
        var result = _sut.Compare(project1, project2);

        // Assert
        result.HasDifferences.Should().BeTrue();
        result.Details.Should().Contain(d =>
            d.Type == "Added" &&
            d.Location == "Programs.SecondProgram");
    }

    [Fact]
    public void Compare_ModifiedProgramLanguage_DetectsModification()
    {
        // Arrange
        var project1 = TestDataBuilder.CreateSampleProject();
        var project2 = TestDataBuilder.CreateSampleProject();
        var modifiedProgram = TestDataBuilder.CreateSampleProgram("MainProgram");
        modifiedProgram = modifiedProgram with { Language = "Structured Text" };
        project2 = project2 with { Programs = new List<ProgramInfo> { modifiedProgram } };

        // Act
        var result = _sut.Compare(project1, project2);

        // Assert
        result.HasDifferences.Should().BeTrue();
        result.Details.Should().Contain(d =>
            d.Type == "Modified" &&
            d.Location == "Programs.MainProgram.Language" &&
            d.Value1 == "Ladder Logic" &&
            d.Value2 == "Structured Text");
    }

    [Fact]
    public void Compare_AddedTag_DetectsAddition()
    {
        // Arrange
        var project1 = TestDataBuilder.CreateSampleProject();
        var project2 = TestDataBuilder.CreateSampleProject();
        var newTag = TestDataBuilder.CreateSampleTag("NewTag", "REAL");
        project2 = project2 with
        {
            Tags = new List<TagInfo> { project2.Tags[0], project2.Tags[1], newTag }
        };

        // Act
        var result = _sut.Compare(project1, project2);

        // Assert
        result.HasDifferences.Should().BeTrue();
        result.Details.Should().Contain(d =>
            d.Type == "Added" &&
            d.Location == "Tags.NewTag");
    }

    [Fact]
    public void Compare_ModifiedTagDataType_DetectsModification()
    {
        // Arrange
        var project1 = TestDataBuilder.CreateSampleProject();
        var project2 = TestDataBuilder.CreateSampleProject();
        var modifiedTag = TestDataBuilder.CreateSampleTag("TestTag", "REAL");
        var secondTag = project2.Tags[1];
        project2 = project2 with
        {
            Tags = new List<TagInfo> { modifiedTag, secondTag }
        };

        // Act
        var result = _sut.Compare(project1, project2);

        // Assert
        result.HasDifferences.Should().BeTrue();
        result.Details.Should().Contain(d =>
            d.Type == "Modified" &&
            d.Location == "Tags.TestTag.DataType" &&
            d.Value1 == "DINT" &&
            d.Value2 == "REAL");
    }

    [Fact]
    public void Compare_ModifiedTagInitialValue_DetectsModification()
    {
        // Arrange
        var project1 = TestDataBuilder.CreateSampleProject();
        var project2 = TestDataBuilder.CreateSampleProject();
        var modifiedTag = TestDataBuilder.CreateSampleTag("TestTag", "DINT");
        modifiedTag = modifiedTag with { InitialValue = "100" };
        var secondTag = project2.Tags[1];
        project2 = project2 with
        {
            Tags = new List<TagInfo> { modifiedTag, secondTag }
        };

        // Act
        var result = _sut.Compare(project1, project2);

        // Assert
        result.HasDifferences.Should().BeTrue();
        result.Details.Should().Contain(d =>
            d.Type == "Modified" &&
            d.Location == "Tags.TestTag.InitialValue" &&
            d.Value1 == "0" &&
            d.Value2 == "100");
    }

    [Fact]
    public void Compare_AddedIoModule_DetectsAddition()
    {
        // Arrange
        var project1 = TestDataBuilder.CreateSampleProject();
        var project2 = TestDataBuilder.CreateSampleProject();
        var newModule = TestDataBuilder.CreateSampleIoModule("OutputModule", "Digital Output");
        project2 = project2 with
        {
            IoModules = new List<IoModuleInfo> { project2.IoModules[0], newModule }
        };

        // Act
        var result = _sut.Compare(project1, project2);

        // Assert
        result.HasDifferences.Should().BeTrue();
        result.Details.Should().Contain(d =>
            d.Type == "Added" &&
            d.Location == "IoModules.OutputModule");
    }

    [Fact]
    public void Compare_ModifiedIoModuleType_DetectsModification()
    {
        // Arrange
        var project1 = TestDataBuilder.CreateSampleProject();
        var project2 = TestDataBuilder.CreateSampleProject();
        var modifiedModule = TestDataBuilder.CreateSampleIoModule("InputModule", "Analog Input");
        project2 = project2 with
        {
            IoModules = new List<IoModuleInfo> { modifiedModule }
        };

        // Act
        var result = _sut.Compare(project1, project2);

        // Assert
        result.HasDifferences.Should().BeTrue();
        result.Details.Should().Contain(d =>
            d.Type == "Modified" &&
            d.Location == "IoModules.InputModule.Type" &&
            d.Value1 == "Digital Input" &&
            d.Value2 == "Analog Input");
    }

    [Fact]
    public void Compare_AddedBlock_DetectsAddition()
    {
        // Arrange
        var project1 = TestDataBuilder.CreateSampleProject();
        var project2 = TestDataBuilder.CreateSampleProject();
        var newBlock = TestDataBuilder.CreateSampleBlock("FC1", 1, "FC");
        project2 = project2 with
        {
            Blocks = new List<BlockInfo> { newBlock }
        };

        // Act
        var result = _sut.Compare(project1, project2);

        // Assert
        result.HasDifferences.Should().BeTrue();
        result.Details.Should().Contain(d =>
            d.Type == "Added" &&
            d.Location == "Blocks.FC1");
    }

    [Fact]
    public void Compare_ModifiedMetadata_DetectsInformationalChange()
    {
        // Arrange
        var project1 = TestDataBuilder.CreateSampleProject();
        var project2 = TestDataBuilder.CreateSampleProject();
        project2 = project2 with
        {
            Metadata = new Dictionary<string, string>
            {
                ["Version"] = "2.0",
                ["Author"] = "Test"
            }
        };

        // Act
        var result = _sut.Compare(project1, project2);

        // Assert
        result.Details.Should().Contain(d =>
            d.Type == "Info" &&
            d.Location == "Metadata.Version" &&
            d.Value1 == "1.0" &&
            d.Value2 == "2.0");
    }

    [Fact]
    public void Compare_MultipleDifferences_GeneratesCorrectSummary()
    {
        // Arrange
        var project1 = TestDataBuilder.CreateSampleProject();
        var project2 = TestDataBuilder.CreateSampleProject();

        // Add multiple differences
        var newTag = TestDataBuilder.CreateSampleTag("NewTag");
        var modifiedProgram = project2.Programs[0] with { Language = "Structured Text" };

        project2 = project2 with
        {
            Tags = new List<TagInfo> { project2.Tags[0], project2.Tags[1], newTag },
            Programs = new List<ProgramInfo> { modifiedProgram },
            Metadata = new Dictionary<string, string>
            {
                ["Version"] = "2.0",
                ["Author"] = "NewAuthor"
            }
        };

        // Act
        var result = _sut.Compare(project1, project2);

        // Assert
        result.HasDifferences.Should().BeTrue();
        result.Summary.Should().Contain("added");
        result.Summary.Should().Contain("modified");
        result.Details.Count.Should().BeGreaterThan(2);
    }

    [Fact]
    public void Compare_NullController_HandlesGracefully()
    {
        // Arrange
        var project1 = TestDataBuilder.CreateSampleProject();
        var project2 = TestDataBuilder.CreateSampleProject();
        project2 = project2 with { Controller = null };

        // Act
        var result = _sut.Compare(project1, project2);

        // Assert
        result.HasDifferences.Should().BeTrue();
        result.Details.Should().Contain(d =>
            d.Type == "Removed" &&
            d.Location == "Controller");
    }

    [Fact]
    public void Compare_BothNullControllers_NoControllerDifferences()
    {
        // Arrange
        var project1 = TestDataBuilder.CreateSampleProject();
        var project2 = TestDataBuilder.CreateSampleProject();
        project1 = project1 with { Controller = null };
        project2 = project2 with { Controller = null };

        // Act
        var result = _sut.Compare(project1, project2);

        // Assert
        result.Details.Should().NotContain(d => d.Location.StartsWith("Controller"));
    }

    [Fact]
    public void Compare_EmptyProjects_ReturnsNoDifferences()
    {
        // Arrange
        var project1 = new PlcProject
        {
            Vendor = "Test",
            Format = "Test",
            FilePath = "test1.txt",
            Controller = null,
            Tasks = new List<TaskInfo>(),
            Programs = new List<ProgramInfo>(),
            Tags = new List<TagInfo>(),
            IoModules = new List<IoModuleInfo>(),
            Blocks = new List<BlockInfo>(),
            Metadata = new Dictionary<string, string>()
        };

        var project2 = new PlcProject
        {
            Vendor = "Test",
            Format = "Test",
            FilePath = "test2.txt",
            Controller = null,
            Tasks = new List<TaskInfo>(),
            Programs = new List<ProgramInfo>(),
            Tags = new List<TagInfo>(),
            IoModules = new List<IoModuleInfo>(),
            Blocks = new List<BlockInfo>(),
            Metadata = new Dictionary<string, string>()
        };

        // Act
        var result = _sut.Compare(project1, project2);

        // Assert
        result.HasDifferences.Should().BeFalse();
    }
}
