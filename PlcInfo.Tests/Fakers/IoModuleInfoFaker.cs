using Bogus;
using PlcInfo.Models;

namespace PlcInfo.Tests.Fakers;

public sealed class IoModuleInfoFaker : Faker<IoModuleInfo>
{
    private static readonly string[] ModuleTypes =
    [
        "Digital Input",
        "Digital Output",
        "Analog Input",
        "Analog Output",
        "Relay Output",
        "Thermocouple Input",
        "RTD Input",
        "High Speed Counter",
        "Servo Drive",
        "Communication Module"
    ];

    private static readonly Dictionary<string, string[]> VendorCatalogs = new()
    {
        ["Allen-Bradley"] = ["1756-IB32", "1756-OB32", "1756-IF8", "1756-OF8", "1756-EN2T", "1769-IQ16", "1769-OW16"],
        ["Siemens"] = ["6ES7321-1BL00", "6ES7322-1BL00", "6ES7331-7KF02", "6ES7332-5HD01", "6ES7340-1CH02"],
        ["Schneider"] = ["140DDI35300", "140DDO35300", "140ACI03000", "140ACO13000", "140NOE77101"],
        ["Beckhoff"] = ["EL1008", "EL2008", "EL3102", "EL4102", "EL6001", "EL7041"]
    };

    public IoModuleInfoFaker()
    {
        CustomInstantiator(f =>
        {
            var vendor = f.PickRandom(VendorCatalogs.Keys.ToArray());
            var catalog = f.PickRandom(VendorCatalogs[vendor]);
            var moduleType = DetermineModuleType(catalog);

            return new IoModuleInfo
            {
                Name = GenerateModuleName(f),
                Type = moduleType,
                CatalogNumber = catalog,
                Vendor = vendor,
                Location = GenerateLocation(f),
                ChannelCount = GetChannelCount(moduleType, f),
                CommFormat = GenerateCommFormat(f, moduleType),
                ParentModule = f.Random.Bool(0.2f) ? $"Rack{f.Random.Int(1, 5)}" : null,
                Configuration = GenerateConfiguration(f, moduleType),
                AssociatedTags = GenerateAssociatedTags(f),
                Properties = GenerateProperties(f)
            };
        });
    }

    private static string GenerateModuleName(Faker f)
    {
        var prefix = f.PickRandom(new[] { "Local", "Remote", "Rack" });
        var slot = f.Random.Int(0, 16);
        return $"{prefix}:{f.Random.Int(1, 10)}:I.{slot}";
    }

    private static string DetermineModuleType(string catalogNumber)
    {
        if (catalogNumber.Contains("DI") || catalogNumber.Contains("IB") || catalogNumber.Contains("IQ") ||
            catalogNumber.Contains("321") || catalogNumber.Contains("1008"))
            return "Digital Input";
        if (catalogNumber.Contains("DO") || catalogNumber.Contains("OB") || catalogNumber.Contains("OW") ||
            catalogNumber.Contains("322") || catalogNumber.Contains("2008"))
            return "Digital Output";
        if (catalogNumber.Contains("IF") || catalogNumber.Contains("ACI") || catalogNumber.Contains("331") ||
            catalogNumber.Contains("3102"))
            return "Analog Input";
        if (catalogNumber.Contains("OF") || catalogNumber.Contains("ACO") || catalogNumber.Contains("332") ||
            catalogNumber.Contains("4102"))
            return "Analog Output";
        if (catalogNumber.Contains("EN") || catalogNumber.Contains("NOE") || catalogNumber.Contains("340") ||
            catalogNumber.Contains("6001"))
            return "Communication Module";
        return "Digital Input";
    }

    private static string GenerateLocation(Faker f)
    {
        return f.Random.Int(0, 2) switch
        {
            0 => $"Rack:{f.Random.Int(0, 7)}, Slot:{f.Random.Int(0, 16)}",
            1 => $"Node:{f.Random.Int(1, 32)}, Slot:{f.Random.Int(0, 8)}",
            _ => $"Local:{f.Random.Int(1, 4)}:{f.Random.Int(0, 12)}"
        };
    }

    private static int GetChannelCount(string moduleType, Faker f)
    {
        return moduleType switch
        {
            "Digital Input" or "Digital Output" => f.PickRandom(new[] { 8, 16, 32 }),
            "Analog Input" or "Analog Output" => f.PickRandom(new[] { 2, 4, 8 }),
            "Communication Module" => 1,
            _ => f.Random.Int(1, 16)
        };
    }

    private static string? GenerateCommFormat(Faker f, string moduleType)
    {
        if (!f.Random.Bool(0.6f)) return null;

        return moduleType.Contains("Analog")
            ? f.PickRandom(new[] { "REAL", "INT", "DINT" })
            : moduleType.Contains("Digital")
                ? "Data"
                : "Listen Only";
    }

    private static Dictionary<string, string> GenerateConfiguration(Faker f, string moduleType)
    {
        var config = new Dictionary<string, string>();

        if (moduleType.Contains("Analog"))
        {
            config["InputRange"] = f.PickRandom(new[] { "0-10V", "4-20mA", "-10 to 10V", "0-20mA" });
            config["Resolution"] = f.PickRandom(new[] { "12-bit", "14-bit", "16-bit" });
        }
        else if (moduleType.Contains("Digital"))
        {
            config["ResponseTime"] = f.PickRandom(new[] { "0.5ms", "1ms", "2ms", "5ms" });
            config["InputVoltage"] = f.PickRandom(new[] { "24VDC", "120VAC", "240VAC" });
        }

        if (f.Random.Bool(0.3f))
        {
            config["FaultMode"] = f.PickRandom(new[] { "Last State", "Off", "User Defined" });
        }

        return config;
    }

    private static IReadOnlyList<string> GenerateAssociatedTags(Faker f)
    {
        var count = f.Random.Int(0, 8);
        var tags = new List<string>();

        for (int i = 0; i < count; i++)
        {
            tags.Add($"IO_Point_{f.Random.Int(0, 31):D2}");
        }

        return tags;
    }

    private static Dictionary<string, string> GenerateProperties(Faker f)
    {
        var props = new Dictionary<string, string>();

        if (f.Random.Bool(0.3f))
        {
            props["FirmwareRevision"] = $"{f.Random.Int(1, 10)}.{f.Random.Int(0, 99):D2}";
        }

        if (f.Random.Bool(0.2f))
        {
            props["SerialNumber"] = f.Random.Hexadecimal(8, "");
        }

        if (f.Random.Bool(0.2f))
        {
            props["Status"] = f.PickRandom(new[] { "Running", "Faulted", "Inhibited", "Configuring" });
        }

        return props;
    }
}