using PlcInfo.Models;

namespace PlcInfo.Tests.TestHelpers;

/// <summary>
/// Builder for creating test PLC project data.
/// </summary>
public class TestDataBuilder
{
    public static PlcProject CreateSampleProject(string filePath = "test.L5X", string vendor = "Rockwell")
    {
        return new PlcProject
        {
            Vendor = vendor,
            Format = "L5X",
            FilePath = filePath,
            LastModified = new DateTime(2024, 1, 1, 12, 0, 0),
            Controller = CreateSampleController(),
            Tasks = new List<TaskInfo> { CreateSampleTask() },
            Programs = new List<ProgramInfo> { CreateSampleProgram() },
            Tags = new List<TagInfo> { CreateSampleTag(), CreateIoTag() },
            IoModules = new List<IoModuleInfo> { CreateSampleIoModule() },
            Blocks = new List<BlockInfo>(),
            Metadata = new Dictionary<string, string>
            {
                ["Version"] = "1.0",
                ["Author"] = "Test"
            }
        };
    }

    public static ControllerInfo CreateSampleController(string name = "TestController", string type = "1756-L83E")
    {
        return new ControllerInfo
        {
            Name = name,
            Type = type,
            Vendor = "Rockwell",
            FirmwareRevision = "32.011",
            CpuType = "L83E",
            Rack = 0,
            Slot = 0,
            NetworkCount = 2,
            Properties = new Dictionary<string, string>()
        };
    }

    public static TaskInfo CreateSampleTask(string name = "MainTask", int priority = 10)
    {
        return new TaskInfo
        {
            Name = name,
            Type = "Continuous",
            Priority = priority,
            Rate = null,
            IsEnabled = true,
            WatchdogTimeout = 500.0,
            ProgramNames = new List<string> { "MainProgram" },
            Properties = new Dictionary<string, string>()
        };
    }

    public static ProgramInfo CreateSampleProgram(string name = "MainProgram")
    {
        return new ProgramInfo
        {
            Name = name,
            Type = "Program",
            Language = "Ladder Logic",
            Description = "Main program",
            TaskName = "MainTask",
            Routines = new List<RoutineInfo> { CreateSampleRoutine() },
            LocalTags = new List<TagInfo>(),
            SizeBytes = 1024,
            Properties = new Dictionary<string, string>()
        };
    }

    public static RoutineInfo CreateSampleRoutine(string name = "MainRoutine")
    {
        return new RoutineInfo
        {
            Name = name,
            Type = "Routine",
            Language = "Ladder Logic",
            RungCount = 10,
            NetworkCount = 0,
            Rungs = new List<RungInfo>(),
            SizeBytes = 512,
            Properties = new Dictionary<string, string>()
        };
    }

    public static TagInfo CreateSampleTag(string name = "TestTag", string dataType = "DINT")
    {
        return new TagInfo
        {
            Name = name,
            DataType = dataType,
            Scope = "Global",
            Usage = "Public",
            Description = "Test tag",
            InitialValue = "0",
            Address = null,
            IsIoTag = false,
            IoModule = null,
            ArrayDimensions = null,
            AliasFor = null,
            Properties = new Dictionary<string, string>()
        };
    }

    public static TagInfo CreateIoTag(string name = "LocalInput", string module = "InputModule")
    {
        return new TagInfo
        {
            Name = name,
            DataType = "BOOL",
            Scope = "Global",
            Usage = "Input",
            Description = "Input tag",
            InitialValue = null,
            Address = "Local:1:I.Data.0",
            IsIoTag = true,
            IoModule = module,
            ArrayDimensions = null,
            AliasFor = null,
            Properties = new Dictionary<string, string>()
        };
    }

    public static IoModuleInfo CreateSampleIoModule(string name = "InputModule", string type = "Digital Input")
    {
        return new IoModuleInfo
        {
            Name = name,
            Type = type,
            CatalogNumber = "1756-IB16",
            Vendor = "Rockwell",
            Location = "Local:1",
            ChannelCount = 16,
            CommFormat = "Data - INT",
            ParentModule = null,
            Configuration = new Dictionary<string, string>(),
            AssociatedTags = new List<string> { "LocalInput" },
            Properties = new Dictionary<string, string>()
        };
    }

    public static BlockInfo CreateSampleBlock(string name = "OB1", int number = 1, string type = "OB")
    {
        return new BlockInfo
        {
            Name = name,
            Number = number,
            Type = type,
            Language = "LAD",
            Title = "Main Organization Block",
            Comment = "Main cycle",
            NetworkCount = 5,
            Interface = new Dictionary<string, string>(),
            SizeBytes = 2048,
            Properties = new Dictionary<string, string>()
        };
    }

    public static MetricsSummary CreateSampleMetrics()
    {
        return new MetricsSummary
        {
            TotalPrograms = 5,
            TotalRoutines = 20,
            TotalRungs = 100,
            TotalBlocks = 0,
            TotalNetworks = 0,
            TotalInstructions = 500,
            TotalTags = 150,
            GlobalTags = 100,
            LocalTags = 50,
            IoTags = 30,
            TotalIoModules = 10,
            InputModules = 5,
            OutputModules = 5,
            TotalTasks = 3,
            ComplexityScore = 45.5,
            LanguageDistribution = new Dictionary<string, int>
            {
                ["Ladder Logic"] = 15,
                ["Structured Text"] = 5
            },
            DataTypeDistribution = new Dictionary<string, int>
            {
                ["DINT"] = 50,
                ["BOOL"] = 40,
                ["REAL"] = 30,
                ["STRING"] = 20,
                ["INT"] = 10
            },
            TotalMemoryBytes = 1024000
        };
    }
}
