using Bogus;
using PlcInfo.Models;

namespace PlcInfo.Tests.Fakers;

public sealed class PlcProjectFaker : Faker<PlcProject>
{
    private readonly ControllerInfoFaker _controllerFaker;
    private readonly TaskInfoFaker _taskFaker;
    private readonly ProgramInfoFaker _programFaker;
    private readonly BlockInfoFaker _blockFaker;
    private readonly TagInfoFaker _tagFaker;
    private readonly IoModuleInfoFaker _ioModuleFaker;

    private static readonly Dictionary<string, string[]> VendorFormats = new()
    {
        ["Allen-Bradley"] = ["L5X", "ACD"],
        ["Siemens"] = ["AP15_1", "AP16", "AP17", "AP18", "S7P", "ZAP13", "ZAP14", "ZAP15", "ZAP16", "ZAP17"],
        ["Schneider"] = ["STU", "STA", "XEF"],
        ["Codesys"] = ["project", "export"],
        ["Beckhoff"] = ["tpy", "tszip", "tnzip"],
        ["Omron"] = ["smc2", "cxp"],
        ["Mitsubishi"] = ["gxw", "gx3"]
    };

    public PlcProjectFaker()
    {
        _controllerFaker = new ControllerInfoFaker();
        _taskFaker = new TaskInfoFaker();
        _programFaker = new ProgramInfoFaker();
        _blockFaker = new BlockInfoFaker();
        _tagFaker = new TagInfoFaker();
        _ioModuleFaker = new IoModuleInfoFaker();

        CustomInstantiator(f =>
        {
            var vendor = f.PickRandom(VendorFormats.Keys.ToArray());
            var format = f.PickRandom(VendorFormats[vendor]);
            var isSiemens = vendor == "Siemens";

            return new PlcProject
            {
                Vendor = vendor,
                Format = format,
                FilePath = GenerateFilePath(f, vendor, format),
                Controller = f.Random.Bool(0.9f) ? GenerateController(f, vendor) : null,
                Tasks = GenerateTasks(f, isSiemens),
                Programs = isSiemens ? Array.Empty<ProgramInfo>() : GeneratePrograms(f),
                Blocks = isSiemens ? GenerateBlocks(f) : Array.Empty<BlockInfo>(),
                Tags = GenerateTags(f),
                IoModules = GenerateIoModules(f),
                LastModified = f.Random.Bool(0.8f) ? f.Date.Recent(30) : null,
                Metadata = GenerateMetadata(f, vendor)
            };
        });
    }

    private static string GenerateFilePath(Faker f, string vendor, string format)
    {
        var projectName = f.PickRandom(new[] { "PlantControl", "MachineLogic", "ProcessAutomation", "LineControl", "TestProject" });
        var extension = format.ToLower();

        var basePath = f.Random.Int(0, 2) switch
        {
            0 => $@"C:\Projects\{projectName}",
            1 => $@"D:\PLC\{vendor}\{projectName}",
            _ => $@"C:\Users\Engineer\Documents\{projectName}"
        };

        return $@"{basePath}\{projectName}.{extension}";
    }

    private ControllerInfo GenerateController(Faker f, string vendor)
    {
        var controller = _controllerFaker.Generate();
        controller.Vendor = vendor;
        return controller;
    }

    private IReadOnlyList<TaskInfo> GenerateTasks(Faker f, bool isSiemens)
    {
        if (isSiemens) return Array.Empty<TaskInfo>();

        var count = f.Random.Int(1, 5);
        var tasks = new List<TaskInfo>();

        for (int i = 0; i < count; i++)
        {
            tasks.Add(_taskFaker.Generate());
        }

        return tasks;
    }

    private IReadOnlyList<ProgramInfo> GeneratePrograms(Faker f)
    {
        var count = f.Random.Int(1, 10);
        var programs = new List<ProgramInfo>();

        for (int i = 0; i < count; i++)
        {
            programs.Add(_programFaker.Generate());
        }

        return programs;
    }

    private IReadOnlyList<BlockInfo> GenerateBlocks(Faker f)
    {
        var count = f.Random.Int(5, 30);
        var blocks = new List<BlockInfo>();

        // Always add at least one OB1 (main)
        var mainBlock = _blockFaker.Generate();
        mainBlock.Type = "OB";
        mainBlock.Number = 1;
        mainBlock.Name = "Main";
        mainBlock.Title = "Main Program";
        blocks.Add(mainBlock);

        for (int i = 1; i < count; i++)
        {
            blocks.Add(_blockFaker.Generate());
        }

        return blocks;
    }

    private IReadOnlyList<TagInfo> GenerateTags(Faker f)
    {
        var count = f.Random.Int(10, 100);
        var tags = new List<TagInfo>();

        for (int i = 0; i < count; i++)
        {
            var tag = _tagFaker.Generate();
            tag.Scope = f.PickRandom(new[] { "Global", "Controller" });
            tags.Add(tag);
        }

        return tags;
    }

    private IReadOnlyList<IoModuleInfo> GenerateIoModules(Faker f)
    {
        var count = f.Random.Int(2, 15);
        var modules = new List<IoModuleInfo>();

        for (int i = 0; i < count; i++)
        {
            modules.Add(_ioModuleFaker.Generate());
        }

        return modules;
    }

    private static Dictionary<string, string> GenerateMetadata(Faker f, string vendor)
    {
        var metadata = new Dictionary<string, string>
        {
            ["CreatedBy"] = f.Name.FullName(),
            ["CreatedDate"] = f.Date.Past(2).ToString("yyyy-MM-dd"),
            ["ProjectVersion"] = $"{f.Random.Int(1, 10)}.{f.Random.Int(0, 99)}.{f.Random.Int(0, 999)}"
        };

        if (f.Random.Bool(0.3f))
        {
            metadata["Company"] = f.Company.CompanyName();
        }

        if (f.Random.Bool(0.3f))
        {
            metadata["Department"] = f.PickRandom(new[] { "Engineering", "Maintenance", "Operations", "R&D" });
        }

        if (vendor == "Allen-Bradley" && f.Random.Bool(0.5f))
        {
            metadata["RSLogix5000Version"] = f.PickRandom(new[] { "30.00", "31.00", "32.00", "33.00", "34.00" });
        }

        if (vendor == "Siemens" && f.Random.Bool(0.5f))
        {
            metadata["TIAPortalVersion"] = f.PickRandom(new[] { "V15.1", "V16", "V17", "V18" });
        }

        if (f.Random.Bool(0.2f))
        {
            metadata["Description"] = f.Lorem.Sentence(5, 15);
        }

        return metadata;
    }
}