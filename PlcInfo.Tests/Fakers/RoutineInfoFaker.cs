using Bogus;
using PlcInfo.Models;

namespace PlcInfo.Tests.Fakers;

public sealed class RoutineInfoFaker : Faker<RoutineInfo>
{
    private static readonly string[] RoutineTypes = ["Main", "Subroutine", "Interrupt", "Fault", "Periodic", "Event"];
    private static readonly string[] Languages = ["Ladder", "ST", "FBD", "SFC", "IL", "LAD", "SCL"];
    private readonly RungInfoFaker _rungFaker;

    public RoutineInfoFaker()
    {
        _rungFaker = new RungInfoFaker();

        CustomInstantiator(f =>
        {
            var rungs = GenerateRungs(f);
            return new RoutineInfo
            {
                Name = GenerateRoutineName(f),
                Type = f.PickRandom(RoutineTypes),
                Language = f.PickRandom(Languages),
                ProgramName = f.Random.Bool(0.8f) ? $"Program{f.Random.Int(1, 10)}" : null,
                Description = f.Random.Bool(0.6f) ? f.Lorem.Sentence(5, 15) : null,
                RungCount = rungs.Count,
                NetworkCount = f.Random.Int(0, 50),
                Rungs = rungs,
                SizeBytes = f.Random.Bool(0.5f) ? f.Random.Long(100, 100000) : null,
                Properties = GenerateProperties(f)
            };
        });
    }

    private static string GenerateRoutineName(Faker f)
    {
        var prefixes = new[] { "Main", "Init", "Alarm", "Control", "Monitor", "Calculate", "Handle", "Process", "Check", "Update" };
        var suffixes = new[] { "Routine", "Logic", "Sequence", "Handler", "Manager", "Controller", "Processor" };

        return f.Random.Bool(0.7f)
            ? $"{f.PickRandom(prefixes)}_{f.PickRandom(suffixes)}"
            : $"R{f.Random.Int(1, 999):D3}_{f.Random.AlphaNumeric(4).ToUpper()}";
    }

    private IReadOnlyList<RungInfo> GenerateRungs(Faker f)
    {
        var count = f.Random.Int(0, 20);
        var rungs = new List<RungInfo>();

        for (int i = 0; i < count; i++)
        {
            var rung = _rungFaker.Generate();
            rung.Number = i;
            rungs.Add(rung);
        }

        return rungs;
    }

    private static Dictionary<string, string> GenerateProperties(Faker f)
    {
        var props = new Dictionary<string, string>();

        if (f.Random.Bool(0.3f))
        {
            props["ExecutionOrder"] = f.Random.Int(1, 100).ToString();
        }

        if (f.Random.Bool(0.2f))
        {
            props["InhibitState"] = f.Random.Bool().ToString();
        }

        if (f.Random.Bool(0.2f))
        {
            props["LastModified"] = f.Date.Past(1).ToString("yyyy-MM-dd HH:mm:ss");
        }

        return props;
    }
}