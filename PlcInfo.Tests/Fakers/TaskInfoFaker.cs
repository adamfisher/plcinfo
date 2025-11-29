using Bogus;
using PlcInfo.Models;

namespace PlcInfo.Tests.Fakers;

public sealed class TaskInfoFaker : Faker<TaskInfo>
{
    private static readonly string[] TaskTypes = ["Continuous", "Periodic", "Event", "Interrupt", "Safety"];

    public TaskInfoFaker()
    {
        CustomInstantiator(f =>
        {
            var taskType = f.PickRandom(TaskTypes);
            var rate = taskType == "Periodic" ? (double?)f.Random.Double(1, 1000) : null;

            return new TaskInfo
            {
                Name = GenerateTaskName(f, taskType),
                Type = taskType,
                Priority = GeneratePriority(f, taskType),
                Rate = rate,
                IsEnabled = f.Random.Bool(0.9f),
                ProgramNames = GenerateProgramNames(f),
                WatchdogTimeout = f.Random.Bool(0.6f) ? f.Random.Double(500, 5000) : null,
                Properties = GenerateProperties(f, taskType)
            };
        });
    }

    private static string GenerateTaskName(Faker f, string taskType)
    {
        var baseNames = taskType switch
        {
            "Continuous" => new[] { "MainTask", "ContinuousTask", "DefaultTask" },
            "Periodic" => new[] { "PeriodicTask", "Scan_100ms", "Scan_10ms", "FastTask", "SlowTask" },
            "Event" => new[] { "EventTask", "AlarmTask", "ChangeOfStateTask" },
            "Interrupt" => new[] { "InterruptTask", "HighPriorityTask", "ISR_Task" },
            "Safety" => new[] { "SafetyTask", "SIL3_Task", "EmergencyTask" },
            _ => new[] { "Task1", "Task2", "Task3" }
        };

        return f.PickRandom(baseNames);
    }

    private static int GeneratePriority(Faker f, string taskType)
    {
        return taskType switch
        {
            "Safety" => f.Random.Int(1, 3),
            "Interrupt" => f.Random.Int(1, 5),
            "Event" => f.Random.Int(5, 10),
            "Periodic" => f.Random.Int(10, 15),
            "Continuous" => f.Random.Int(15, 20),
            _ => f.Random.Int(1, 20)
        };
    }

    private static IReadOnlyList<string> GenerateProgramNames(Faker f)
    {
        var count = f.Random.Int(1, 5);
        var programs = new List<string>();
        var programNames = new[]
        {
            "MainProgram",
            "MotorControl",
            "ValveControl",
            "AlarmHandler",
            "DataCollection",
            "ProcessControl",
            "SafetyProgram",
            "CommunicationHandler"
        };

        for (int i = 0; i < count; i++)
        {
            programs.Add(f.PickRandom(programNames) + (f.Random.Bool(0.3f) ? $"_{f.Random.Int(1, 10)}" : ""));
        }

        return programs.Distinct().ToList();
    }

    private static Dictionary<string, string> GenerateProperties(Faker f, string taskType)
    {
        var props = new Dictionary<string, string>();

        if (taskType == "Periodic" && f.Random.Bool(0.5f))
        {
            props["MaxInterval"] = f.Random.Double(100, 2000).ToString("F2") + "ms";
            props["MinInterval"] = f.Random.Double(1, 100).ToString("F2") + "ms";
        }

        if (f.Random.Bool(0.3f))
        {
            props["LastExecutionTime"] = f.Random.Double(0.1, 50).ToString("F3") + "ms";
        }

        if (f.Random.Bool(0.2f))
        {
            props["OverlapCount"] = f.Random.Int(0, 5).ToString();
        }

        if (taskType == "Event" && f.Random.Bool(0.4f))
        {
            props["EventTag"] = $"Event_{f.Random.AlphaNumeric(4).ToUpper()}";
        }

        return props;
    }
}