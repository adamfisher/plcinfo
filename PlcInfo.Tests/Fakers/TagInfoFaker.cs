using Bogus;
using PlcInfo.Models;

namespace PlcInfo.Tests.Fakers;

public sealed class TagInfoFaker : Faker<TagInfo>
{
    private static readonly string[] DataTypes = ["BOOL", "INT", "DINT", "SINT", "REAL", "STRING", "TIMER", "COUNTER", "DWORD", "WORD", "BYTE", "ARRAY[0..9] OF INT", "UDT_Motor", "UDT_Valve"];
    private static readonly string[] Scopes = ["Global", "Local", "Program", "Controller"];
    private static readonly string[] Usages = ["Input", "Output", "Local", "Public", "InOut"];

    public TagInfoFaker()
    {
        CustomInstantiator(f => new TagInfo
        {
            Name = GenerateTagName(f),
            DataType = f.PickRandom(DataTypes),
            Scope = f.PickRandom(Scopes),
            Usage = f.Random.Bool(0.7f) ? f.PickRandom(Usages) : null,
            Description = f.Random.Bool(0.6f) ? f.Lorem.Sentence(3, 8) : null,
            InitialValue = GenerateInitialValue(f),
            Address = GenerateAddress(f),
            IsIoTag = f.Random.Bool(0.3f),
            IoModule = null,
            ArrayDimensions = GenerateArrayDimensions(f),
            AliasFor = f.Random.Bool(0.1f) ? $"Tag_{f.Random.Int(1, 100)}" : null,
            Properties = GenerateProperties(f)
        });

        // Set IoModule if IsIoTag is true
        RuleFor(t => t.IoModule, (f, t) => t.IsIoTag ? $"Module{f.Random.Int(1, 10)}" : null);
    }

    private static string GenerateTagName(Faker f)
    {
        var prefix = f.PickRandom("HMI", "PLC", "IO", "Motor", "Valve", "Sensor", "Alarm", "Status", "Config", "Temp");
        var suffix = f.Random.Bool(0.5f) ? $"_{f.Random.Int(1, 99):D2}" : $"_{f.Random.AlphaNumeric(3).ToUpper()}";
        return prefix + suffix;
    }

    private static string? GenerateInitialValue(Faker f)
    {
        if (!f.Random.Bool(0.3f)) return null;

        return f.Random.Int(0, 3) switch
        {
            0 => f.Random.Int(0, 100).ToString(),
            1 => f.Random.Bool().ToString(),
            2 => f.Random.Float(0, 100).ToString("F2"),
            _ => "0"
        };
    }

    private static string? GenerateAddress(Faker f)
    {
        if (!f.Random.Bool(0.4f)) return null;

        var addressType = f.Random.Int(0, 3);
        return addressType switch
        {
            0 => $"I:{f.Random.Int(0, 7)}/{f.Random.Int(0, 15)}",
            1 => $"O:{f.Random.Int(0, 7)}/{f.Random.Int(0, 15)}",
            2 => $"N{f.Random.Int(7, 100)}:{f.Random.Int(0, 255)}",
            _ => $"%MW{f.Random.Int(0, 9999)}"
        };
    }

    private static int[]? GenerateArrayDimensions(Faker f)
    {
        if (!f.Random.Bool(0.2f)) return null;

        var dimensions = f.Random.Int(1, 3);
        var result = new int[dimensions];
        for (int i = 0; i < dimensions; i++)
        {
            result[i] = f.Random.Int(1, 100);
        }
        return result;
    }

    private static Dictionary<string, string> GenerateProperties(Faker f)
    {
        var props = new Dictionary<string, string>();

        if (f.Random.Bool(0.3f))
        {
            props["Engineering Unit"] = f.PickRandom(new[] { "°C", "°F", "PSI", "Bar", "%", "RPM", "Hz" });
        }

        if (f.Random.Bool(0.2f))
        {
            props["Min"] = f.Random.Int(0, 50).ToString();
            props["Max"] = f.Random.Int(51, 100).ToString();
        }

        return props;
    }
}