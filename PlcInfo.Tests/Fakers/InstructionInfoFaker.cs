using Bogus;
using PlcInfo.Models;

namespace PlcInfo.Tests.Fakers;

public sealed class InstructionInfoFaker : Faker<InstructionInfo>
{
    private static readonly string[] CommonMnemonics = ["XIC", "XIO", "OTE", "OTL", "OTU", "TON", "TOF", "CTU", "CTD", "MOV", "ADD", "SUB", "MUL", "DIV", "EQU", "NEQ", "LES", "GRT", "LEQ", "GEQ", "JSR", "RET", "AFI"];
    private static readonly string[] InstructionTypes = ["Input", "Output", "Timer", "Counter", "Math", "Compare", "Program Control", "Data Handling"];

    public InstructionInfoFaker()
    {
        CustomInstantiator(f => new InstructionInfo
        {
            Mnemonic = f.PickRandom(CommonMnemonics),
            Type = f.PickRandom(InstructionTypes),
            Operands = GenerateOperands(f),
            Position = f.Random.Int(0, 100),
            Properties = GenerateProperties(f)
        });
    }

    private static IReadOnlyList<string> GenerateOperands(Faker f)
    {
        var count = f.Random.Int(0, 3);
        var operands = new List<string>();

        for (int i = 0; i < count; i++)
        {
            var operandType = f.Random.Int(0, 3);
            operands.Add(operandType switch
            {
                0 => $"Tag{f.Random.Int(1, 100)}",
                1 => f.Random.Int(0, 32767).ToString(),
                2 => $"Timer{f.Random.Int(1, 10)}.DN",
                _ => $"Local:{f.Random.Int(0, 10)}:I.Data.{f.Random.Int(0, 15)}"
            });
        }

        return operands;
    }

    private static Dictionary<string, string> GenerateProperties(Faker f)
    {
        var props = new Dictionary<string, string>();

        if (f.Random.Bool(0.3f))
        {
            props["Comment"] = f.Lorem.Sentence(3, 5);
        }

        if (f.Random.Bool(0.2f))
        {
            props["Preset"] = f.Random.Int(100, 10000).ToString();
        }

        return props;
    }
}