using Bogus;
using PlcInfo.Models;

namespace PlcInfo.Tests.Fakers;

public sealed class ControllerInfoFaker : Faker<ControllerInfo>
{
    private static readonly Dictionary<string, string[]> VendorControllers = new()
    {
        ["Allen-Bradley"] = ["ControlLogix 5580", "CompactLogix 5380", "CompactLogix 5370", "MicroLogix 1400", "PLC-5"],
        ["Siemens"] = ["S7-1500", "S7-1200", "S7-300", "S7-400", "ET 200SP"],
        ["Schneider"] = ["Modicon M580", "Modicon M340", "Modicon Quantum", "Modicon M221"],
        ["Beckhoff"] = ["CX9020", "CX5140", "CX2040", "CX1020"],
        ["Omron"] = ["NJ501", "NJ301", "CP1H", "CJ2M"],
        ["Mitsubishi"] = ["iQ-R Series", "iQ-F Series", "Q Series", "L Series"]
    };

    public ControllerInfoFaker()
    {
        CustomInstantiator(f =>
        {
            var vendor = f.PickRandom(VendorControllers.Keys.ToArray());
            var controllerType = f.PickRandom(VendorControllers[vendor]);

            return new ControllerInfo
            {
                Name = GenerateControllerName(f),
                Type = controllerType,
                Vendor = vendor,
                FirmwareRevision = GenerateFirmwareRevision(f),
                CpuType = vendor == "Siemens" ? GenerateSiemensCpuType(f) : null,
                Rack = f.Random.Bool(0.5f) ? f.Random.Int(0, 7) : null,
                Slot = f.Random.Bool(0.5f) ? f.Random.Int(0, 16) : null,
                NetworkCount = f.Random.Int(0, 5),
                Properties = GenerateProperties(f, vendor)
            };
        });
    }

    private static string GenerateControllerName(Faker f)
    {
        var names = new[]
        {
            "MainController",
            "Controller1",
            "PLC_Main",
            "ProcessController",
            "SafetyController",
            "RemoteController"
        };

        return f.Random.Bool(0.7f)
            ? f.PickRandom(names)
            : $"PLC_{f.Random.AlphaNumeric(4).ToUpper()}";
    }

    private static string GenerateFirmwareRevision(Faker f)
    {
        return f.Random.Int(0, 2) switch
        {
            0 => $"{f.Random.Int(20, 35)}.{f.Random.Int(0, 99):D2}",
            1 => $"v{f.Random.Int(1, 10)}.{f.Random.Int(0, 9)}.{f.Random.Int(0, 99)}",
            _ => $"{f.Random.Int(1, 15)}.{f.Random.Int(0, 9)}"
        };
    }

    private static string GenerateSiemensCpuType(Faker f)
    {
        var cpuTypes = new[]
        {
            "CPU 1516-3 PN/DP",
            "CPU 1512C-1 PN",
            "CPU 1214C DC/DC/DC",
            "CPU 1215C DC/DC/DC",
            "CPU 315-2 PN/DP",
            "CPU 317-2 PN/DP",
            "CPU 414-3 PN/DP"
        };

        return f.PickRandom(cpuTypes);
    }

    private static Dictionary<string, string> GenerateProperties(Faker f, string vendor)
    {
        var props = new Dictionary<string, string>();

        if (f.Random.Bool(0.3f))
        {
            props["SerialNumber"] = f.Random.Hexadecimal(12, "").ToUpper();
        }

        if (f.Random.Bool(0.3f))
        {
            props["Memory"] = $"{f.Random.Int(1, 32)}MB";
        }

        if (vendor == "Allen-Bradley" && f.Random.Bool(0.4f))
        {
            props["ProcessorType"] = f.PickRandom(new[] { "1756-L85E", "1756-L73", "1769-L33ER", "1769-L24ER" });
        }

        if (vendor == "Siemens" && f.Random.Bool(0.4f))
        {
            props["WorkMemory"] = $"{f.Random.Int(50, 500)}KB";
            props["LoadMemory"] = $"{f.Random.Int(1, 10)}MB";
        }

        if (f.Random.Bool(0.2f))
        {
            props["SafetyLevel"] = f.PickRandom(new[] { "SIL2", "SIL3", "PLd", "PLe" });
        }

        return props;
    }
}