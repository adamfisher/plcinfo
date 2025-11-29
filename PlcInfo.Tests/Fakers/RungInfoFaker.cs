using Bogus;
using PlcInfo.Models;

namespace PlcInfo.Tests.Fakers;

public sealed class RungInfoFaker : Faker<RungInfo>
{
    private readonly InstructionInfoFaker _instructionFaker;

    public RungInfoFaker()
    {
        _instructionFaker = new InstructionInfoFaker();

        CustomInstantiator(f => new RungInfo
        {
            Number = f.IndexFaker,
            Comment = f.Random.Bool(0.6f) ? f.Lorem.Sentence(5, 10) : null,
            Instructions = GenerateInstructions(f),
            Text = GenerateRungText(f),
            Properties = GenerateProperties(f)
        });
    }

    private IReadOnlyList<InstructionInfo> GenerateInstructions(Faker f)
    {
        var count = f.Random.Int(1, 8);
        var instructions = new List<InstructionInfo>();

        for (int i = 0; i < count; i++)
        {
            var instruction = _instructionFaker.Generate();
            instruction.Position = i;
            instructions.Add(instruction);
        }

        return instructions;
    }

    private static string? GenerateRungText(Faker f)
    {
        if (!f.Random.Bool(0.3f)) return null;

        var components = new[]
        {
            "XIC(StartButton)",
            "XIO(StopButton)",
            "OTE(Motor1)",
            "TON(Timer1, 5000)",
            "CTU(Counter1, 100)",
            "[MOV(SourceTag, DestTag)]"
        };

        var count = f.Random.Int(2, 5);
        var selected = new List<string>();
        for (int i = 0; i < count; i++)
        {
            selected.Add(f.PickRandom(components));
        }

        return string.Join(" ", selected);
    }

    private static Dictionary<string, string> GenerateProperties(Faker f)
    {
        var props = new Dictionary<string, string>();

        if (f.Random.Bool(0.2f))
        {
            props["ExecutionTime"] = f.Random.Int(1, 100) + "us";
        }

        if (f.Random.Bool(0.1f))
        {
            props["Modified"] = f.Date.Past(1).ToString("yyyy-MM-dd");
        }

        return props;
    }
}