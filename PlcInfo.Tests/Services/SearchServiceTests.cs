using FluentAssertions;
using PlcInfo.Formatters;
using PlcInfo.Models;
using PlcInfo.Services;
using PlcInfo.Tests.Fakers;
using Xunit.Categories;

namespace PlcInfo.Tests.Services;

[UnitTest]
public class SearchServiceTests
{
    private readonly SearchService _service;
    private readonly PlcProjectFaker _projectFaker;
    private readonly TagInfoFaker _tagFaker;
    private readonly RungInfoFaker _rungFaker;
    private readonly InstructionInfoFaker _instructionFaker;

    public SearchServiceTests()
    {
        _service = new SearchService();
        _projectFaker = new PlcProjectFaker();
        _tagFaker = new TagInfoFaker();
        _rungFaker = new RungInfoFaker();
        _instructionFaker = new InstructionInfoFaker();
    }

    [Fact]
    public void Search_WithEmptySearchText_ReturnsEmptyResults()
    {
        // Arrange
        var project = _projectFaker.Generate();

        // Act
        var results = _service.Search(project, "");
        var resultsNull = _service.Search(project, null!);
        var resultsWhitespace = _service.Search(project, "   ");

        // Assert
        results.Should().BeEmpty();
        resultsNull.Should().BeEmpty();
        resultsWhitespace.Should().BeEmpty();
    }

    [Fact]
    public void Search_ForTags_FindsMatchingTags()
    {
        // Arrange
        var project = new PlcProject
        {
            Vendor = "Test",
            Format = "Test",
            FilePath = "test.plc",
            Tags = new List<TagInfo>
            {
                new() { Name = "Motor1_Start", DataType = "BOOL", Description = "Start motor 1" },
                new() { Name = "Motor1_Stop", DataType = "BOOL", Description = "Stop motor 1" },
                new() { Name = "Motor2_Start", DataType = "BOOL", Description = "Start motor 2" },
                new() { Name = "Pump_Status", DataType = "INT", Description = "Pump status value" }
            }
        };

        // Act
        var results = _service.Search(project, "Motor", "tag").ToList();

        // Assert
        results.Should().HaveCount(3);
        results.Should().AllSatisfy(r => r.Type.Should().Be("Tag"));
        results.Should().Contain(r => r.Name == "Motor1_Start");
        results.Should().Contain(r => r.Name == "Motor1_Stop");
        results.Should().Contain(r => r.Name == "Motor2_Start");
    }

    [Fact]
    public void Search_ForTagsWithDescription_FindsMatchingTagsByDescription()
    {
        // Arrange
        var project = new PlcProject
        {
            Vendor = "Test",
            Format = "Test",
            FilePath = "test.plc",
            Tags = new List<TagInfo>
            {
                new() { Name = "Tag1", DataType = "BOOL", Description = "Emergency stop button" },
                new() { Name = "Tag2", DataType = "BOOL", Description = "Start button" },
                new() { Name = "Tag3", DataType = "INT", Description = "Temperature sensor" },
                new() { Name = "Tag4", DataType = "REAL", Description = null }
            }
        };

        // Act
        var results = _service.Search(project, "button", "tag").ToList();

        // Assert
        results.Should().HaveCount(2);
        results.Should().Contain(r => r.Name == "Tag1");
        results.Should().Contain(r => r.Name == "Tag2");
    }

    [Fact]
    public void Search_ForComments_FindsMatchingComments()
    {
        // Arrange
        var rung1 = new RungInfo
        {
            Number = 0,
            Comment = "Initialize system parameters",
            Instructions = new List<InstructionInfo>()
        };

        var rung2 = new RungInfo
        {
            Number = 1,
            Comment = "Start motor control sequence",
            Instructions = new List<InstructionInfo>()
        };

        var rung3 = new RungInfo
        {
            Number = 2,
            Comment = null,
            Instructions = new List<InstructionInfo>()
        };

        var project = new PlcProject
        {
            Vendor = "Test",
            Format = "Test",
            FilePath = "test.plc",
            Programs = new List<ProgramInfo>
            {
                new()
                {
                    Name = "MainProgram",
                    Type = "Program",
                    Language = "Ladder",
                    Routines = new List<RoutineInfo>
                    {
                        new()
                        {
                            Name = "MainRoutine",
                            Type = "Main",
                            Language = "Ladder",
                            Rungs = new List<RungInfo> { rung1, rung2, rung3 }
                        }
                    }
                }
            }
        };

        // Act
        var results = _service.Search(project, "motor", "comment").ToList();

        // Assert
        results.Should().HaveCount(1);
        results[0].Type.Should().Be("Comment");
        results[0].Context.Should().Contain("Start motor control sequence");
        results[0].Location.Should().Contain("MainProgram");
        results[0].Location.Should().Contain("MainRoutine");
    }

    [Fact]
    public void Search_ForInstructions_FindsMatchingInstructions()
    {
        // Arrange
        var instruction1 = new InstructionInfo
        {
            Mnemonic = "XIC",
            Type = "Input",
            Operands = new[] { "Motor_Start" },
            Position = 0
        };

        var instruction2 = new InstructionInfo
        {
            Mnemonic = "OTE",
            Type = "Output",
            Operands = new[] { "Motor_Run" },
            Position = 1
        };

        var instruction3 = new InstructionInfo
        {
            Mnemonic = "TON",
            Type = "Timer",
            Operands = new[] { "DelayTimer", "5000" },
            Position = 2
        };

        var rung = new RungInfo
        {
            Number = 0,
            Instructions = new List<InstructionInfo> { instruction1, instruction2, instruction3 }
        };

        var project = new PlcProject
        {
            Vendor = "Test",
            Format = "Test",
            FilePath = "test.plc",
            Programs = new List<ProgramInfo>
            {
                new()
                {
                    Name = "MainProgram",
                    Type = "Program",
                    Language = "Ladder",
                    Routines = new List<RoutineInfo>
                    {
                        new()
                        {
                            Name = "MainRoutine",
                            Type = "Main",
                            Language = "Ladder",
                            Rungs = new List<RungInfo> { rung }
                        }
                    }
                }
            }
        };

        // Act
        var results = _service.Search(project, "Motor", "instruction").ToList();

        // Assert
        results.Should().HaveCount(2);
        results.Should().AllSatisfy(r => r.Type.Should().Be("Instruction"));
        results.Should().Contain(r => r.Context.Contains("Motor_Start"));
        results.Should().Contain(r => r.Context.Contains("Motor_Run"));
    }

    [Fact]
    public void Search_WithNoKindSpecified_SearchesEverything()
    {
        // Arrange
        var project = new PlcProject
        {
            Vendor = "Test",
            Format = "Test",
            FilePath = "test.plc",
            Tags = new List<TagInfo>
            {
                new() { Name = "TestTag", DataType = "BOOL", Description = "A test tag" }
            },
            Programs = new List<ProgramInfo>
            {
                new()
                {
                    Name = "MainProgram",
                    Type = "Program",
                    Language = "Ladder",
                    Routines = new List<RoutineInfo>
                    {
                        new()
                        {
                            Name = "MainRoutine",
                            Type = "Main",
                            Language = "Ladder",
                            Rungs = new List<RungInfo>
                            {
                                new()
                                {
                                    Number = 0,
                                    Comment = "Test comment",
                                    Instructions = new List<InstructionInfo>
                                    {
                                        new()
                                        {
                                            Mnemonic = "XIC",
                                            Type = "Input",
                                            Operands = new[] { "TestOperand" },
                                            Position = 0
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        // Act
        var results = _service.Search(project, "test").ToList();

        // Assert
        results.Should().HaveCountGreaterThan(0);
        results.Should().Contain(r => r.Type == "Tag");
        results.Should().Contain(r => r.Type == "Comment");
        results.Should().Contain(r => r.Type == "Instruction");
    }

    [Fact]
    public void Search_IsCaseInsensitive()
    {
        // Arrange
        var project = new PlcProject
        {
            Vendor = "Test",
            Format = "Test",
            FilePath = "test.plc",
            Tags = new List<TagInfo>
            {
                new() { Name = "MOTOR_START", DataType = "BOOL" },
                new() { Name = "motor_stop", DataType = "BOOL" },
                new() { Name = "Motor_Run", DataType = "BOOL" }
            }
        };

        // Act
        var resultsLower = _service.Search(project, "motor", "tag").ToList();
        var resultsUpper = _service.Search(project, "MOTOR", "tag").ToList();
        var resultsMixed = _service.Search(project, "MoToR", "tag").ToList();

        // Assert
        resultsLower.Should().HaveCount(3);
        resultsUpper.Should().HaveCount(3);
        resultsMixed.Should().HaveCount(3);
    }

    [Fact]
    public void Search_WithInvalidKind_ReturnsEmptyResults()
    {
        // Arrange
        var project = _projectFaker.Generate();

        // Act
        var results = _service.Search(project, "test", "invalid_kind").ToList();

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void Search_InProgramLocalTags_FindsMatchingTags()
    {
        // Arrange
        var localTag1 = new TagInfo { Name = "LocalMotor", DataType = "BOOL", Scope = "Local" };
        var localTag2 = new TagInfo { Name = "LocalPump", DataType = "INT", Scope = "Local" };

        var project = new PlcProject
        {
            Vendor = "Test",
            Format = "Test",
            FilePath = "test.plc",
            Programs = new List<ProgramInfo>
            {
                new()
                {
                    Name = "MainProgram",
                    Type = "Program",
                    Language = "Ladder",
                    LocalTags = new List<TagInfo> { localTag1, localTag2 }
                }
            }
        };

        // Act
        var results = _service.Search(project, "local", "tag").ToList();

        // Assert
        results.Should().HaveCount(2);
        results.Should().Contain(r => r.Name == "LocalMotor");
        results.Should().Contain(r => r.Name == "LocalPump");
        results.Should().AllSatisfy(r => r.Location.Should().Contain("MainProgram"));
    }
}