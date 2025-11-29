using Bogus;
using PlcInfo.Models;

namespace PlcInfo.Tests.Fakers;

public sealed class BlockInfoFaker : Faker<BlockInfo>
{
    private static readonly string[] BlockTypes = ["OB", "FB", "FC", "DB"];
    private static readonly string[] Languages = ["LAD", "FBD", "STL", "SCL", "GRAPH"];

    public BlockInfoFaker()
    {
        CustomInstantiator(f =>
        {
            var blockType = f.PickRandom(BlockTypes);
            var blockNumber = GenerateBlockNumber(f, blockType);
            var language = blockType == "DB" ? "DB" : f.PickRandom(Languages);

            return new BlockInfo
            {
                Name = GenerateBlockName(f, blockType, blockNumber),
                Number = blockNumber,
                Type = blockType,
                Language = language,
                Title = f.Random.Bool(0.7f) ? GenerateTitle(f, blockType) : null,
                Comment = f.Random.Bool(0.6f) ? f.Lorem.Sentence(5, 15) : null,
                NetworkCount = blockType == "DB" ? 0 : f.Random.Int(1, 50),
                Interface = GenerateInterface(f, blockType),
                SizeBytes = f.Random.Bool(0.5f) ? f.Random.Long(100, 50000) : null,
                Properties = GenerateProperties(f, blockType)
            };
        });
    }

    private static int GenerateBlockNumber(Faker f, string blockType)
    {
        return blockType switch
        {
            "OB" => f.Random.Bool(0.3f) ? f.PickRandom(new[] { 1, 35, 82, 86, 100, 121, 122 }) : f.Random.Int(1, 999),
            "FB" => f.Random.Int(1, 65535),
            "FC" => f.Random.Int(1, 65535),
            "DB" => f.Random.Int(1, 65535),
            _ => f.Random.Int(1, 999)
        };
    }

    private static string GenerateBlockName(Faker f, string blockType, int blockNumber)
    {
        var customNames = blockType switch
        {
            "OB" => blockNumber switch
            {
                1 => "Main",
                35 => "CyclicInterrupt100ms",
                82 => "DiagnosticError",
                86 => "RackError",
                100 => "StartupComplete",
                121 => "ProgrammingError",
                122 => "IOAccessError",
                _ => $"OB_{blockNumber}"
            },
            "FB" => f.Random.Bool(0.6f) ? f.PickRandom(new[] { "MotorControl", "ValveControl", "PIDController", "AlarmHandler", "DataLogger", "Recipe", "BatchControl" }) : $"FB_{blockNumber}",
            "FC" => f.Random.Bool(0.6f) ? f.PickRandom(new[] { "Calculate", "Convert", "Scale", "Limit", "Average", "Totalize", "Compare" }) : $"FC_{blockNumber}",
            "DB" => f.Random.Bool(0.6f) ? f.PickRandom(new[] { "GlobalData", "RecipeData", "AlarmData", "ConfigData", "MotorData", "ProcessData" }) : $"DB_{blockNumber}",
            _ => $"{blockType}_{blockNumber}"
        };

        return customNames;
    }

    private static string GenerateTitle(Faker f, string blockType)
    {
        return blockType switch
        {
            "OB" => f.PickRandom(new[] { "Main Program", "Cyclic Interrupt", "Startup", "Error Handler", "Time Interrupt" }),
            "FB" => f.PickRandom(new[] { "Motor Control Block", "Valve Control Block", "PID Controller", "Alarm Management", "Data Processing" }),
            "FC" => f.PickRandom(new[] { "Mathematical Function", "Data Conversion", "Scaling Function", "Utility Function", "Communication Handler" }),
            "DB" => f.PickRandom(new[] { "Global Data Block", "Instance Data Block", "Recipe Data", "Configuration Data", "Array Data" }),
            _ => "Block Title"
        };
    }

    private static Dictionary<string, string> GenerateInterface(Faker f, string blockType)
    {
        var interfaceData = new Dictionary<string, string>();

        if (blockType == "FB" || blockType == "FC")
        {
            // Input parameters
            var inputCount = f.Random.Int(0, 5);
            for (int i = 0; i < inputCount; i++)
            {
                var paramName = $"In_{f.PickRandom(new[] { "Enable", "Start", "Stop", "SetPoint", "Value", "Command" })}_{i}";
                var paramType = f.PickRandom(new[] { "BOOL", "INT", "REAL", "TIME", "DINT" });
                interfaceData[$"Input.{paramName}"] = paramType;
            }

            // Output parameters
            var outputCount = f.Random.Int(0, 3);
            for (int i = 0; i < outputCount; i++)
            {
                var paramName = $"Out_{f.PickRandom(new[] { "Done", "Error", "Result", "Status", "Active" })}_{i}";
                var paramType = f.PickRandom(new[] { "BOOL", "INT", "REAL", "WORD", "DINT" });
                interfaceData[$"Output.{paramName}"] = paramType;
            }

            // InOut parameters
            if (f.Random.Bool(0.3f))
            {
                var inOutCount = f.Random.Int(1, 2);
                for (int i = 0; i < inOutCount; i++)
                {
                    var paramName = $"InOut_{f.PickRandom(new[] { "Data", "Buffer", "Array" })}_{i}";
                    var paramType = f.PickRandom(new[] { "ANY", "ARRAY[0..99] OF INT", "STRUCT" });
                    interfaceData[$"InOut.{paramName}"] = paramType;
                }
            }

            // Static variables (FB only)
            if (blockType == "FB" && f.Random.Bool(0.5f))
            {
                var staticCount = f.Random.Int(1, 3);
                for (int i = 0; i < staticCount; i++)
                {
                    var varName = $"Static_{f.PickRandom(new[] { "State", "Counter", "Timer", "LastValue" })}_{i}";
                    var varType = f.PickRandom(new[] { "INT", "BOOL", "TIME", "REAL" });
                    interfaceData[$"Static.{varName}"] = varType;
                }
            }
        }
        else if (blockType == "DB")
        {
            // Data block variables
            var varCount = f.Random.Int(1, 10);
            for (int i = 0; i < varCount; i++)
            {
                var varName = f.PickRandom(new[] { "Tag", "Value", "SetPoint", "Status", "Config", "Data" }) + $"_{i}";
                var varType = f.PickRandom(new[] { "BOOL", "INT", "REAL", "STRING[254]", "ARRAY[0..9] OF REAL", "STRUCT" });
                interfaceData[varName] = varType;
            }
        }

        return interfaceData;
    }

    private static Dictionary<string, string> GenerateProperties(Faker f, string blockType)
    {
        var props = new Dictionary<string, string>();

        if (f.Random.Bool(0.3f))
        {
            props["Author"] = f.Name.FullName();
        }

        if (f.Random.Bool(0.3f))
        {
            props["Family"] = f.PickRandom(new[] { "Motor", "Valve", "Analog", "Communication", "Safety", "Utility" });
        }

        if (f.Random.Bool(0.2f))
        {
            props["Version"] = $"{f.Random.Int(0, 2)}.{f.Random.Int(0, 9)}";
        }

        if (blockType == "FB" && f.Random.Bool(0.3f))
        {
            props["MultiInstance"] = f.Random.Bool().ToString();
        }

        if (blockType == "DB" && f.Random.Bool(0.3f))
        {
            props["Retentive"] = f.Random.Bool().ToString();
            props["Unlinked"] = f.Random.Bool(0.2f).ToString();
        }

        if (blockType == "OB" && f.Random.Bool(0.4f))
        {
            props["EventClass"] = f.PickRandom(new[] { "Cyclic", "Interrupt", "Error", "Startup" });
        }

        return props;
    }
}