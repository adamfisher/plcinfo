using Bogus;
using PlcInfo.Models;

namespace PlcInfo.Tests.Fakers;

public sealed class ProgramInfoFaker : Faker<ProgramInfo>
{
    private static readonly string[] ProgramTypes = ["Program", "Function Block", "Function", "Add-On Instruction", "Equipment Phase"];
    private static readonly string[] Languages = ["Ladder", "ST", "FBD", "SFC", "IL"];
    private readonly RoutineInfoFaker _routineFaker;
    private readonly TagInfoFaker _tagFaker;

    public ProgramInfoFaker()
    {
        _routineFaker = new RoutineInfoFaker();
        _tagFaker = new TagInfoFaker();

        CustomInstantiator(f => new ProgramInfo
        {
            Name = GenerateProgramName(f),
            Type = f.PickRandom(ProgramTypes),
            Language = f.PickRandom(Languages),
            Description = f.Random.Bool(0.7f) ? f.Lorem.Sentence(5, 20) : null,
            TaskName = f.Random.Bool(0.6f) ? $"Task{f.Random.Int(1, 5)}" : null,
            Routines = GenerateRoutines(f),
            LocalTags = GenerateLocalTags(f),
            SizeBytes = f.Random.Bool(0.5f) ? f.Random.Long(1000, 500000) : null,
            Properties = GenerateProperties(f)
        });
    }

    private static string GenerateProgramName(Faker f)
    {
        var names = new[]
        {
            "MainProgram",
            "SafetyProgram",
            "MotorControl",
            "ValveControl",
            "AlarmHandler",
            "DataCollection",
            "ProcessControl",
            "BatchManager",
            "RecipeHandler",
            "Communications"
        };

        return f.Random.Bool(0.7f)
            ? f.PickRandom(names)
            : $"Prog_{f.Random.AlphaNumeric(6).ToUpper()}";
    }

    private IReadOnlyList<RoutineInfo> GenerateRoutines(Faker f)
    {
        var count = f.Random.Int(1, 10);
        var routines = new List<RoutineInfo>();

        for (int i = 0; i < count; i++)
        {
            routines.Add(_routineFaker.Generate());
        }

        return routines;
    }

    private IReadOnlyList<TagInfo> GenerateLocalTags(Faker f)
    {
        var count = f.Random.Int(0, 30);
        var tags = new List<TagInfo>();

        for (int i = 0; i < count; i++)
        {
            var tag = _tagFaker.Generate();
            tag.Scope = "Local";
            tags.Add(tag);
        }

        return tags;
    }

    private static Dictionary<string, string> GenerateProperties(Faker f)
    {
        var props = new Dictionary<string, string>();

        if (f.Random.Bool(0.3f))
        {
            props["Version"] = $"{f.Random.Int(1, 10)}.{f.Random.Int(0, 99)}";
        }

        if (f.Random.Bool(0.2f))
        {
            props["LastDownload"] = f.Date.Past(1).ToString("yyyy-MM-dd HH:mm:ss");
        }

        if (f.Random.Bool(0.2f))
        {
            props["TestEditMode"] = f.Random.Bool().ToString();
        }

        return props;
    }
}