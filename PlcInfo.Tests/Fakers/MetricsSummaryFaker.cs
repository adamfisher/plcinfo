using Bogus;
using PlcInfo.Models;

namespace PlcInfo.Tests.Fakers;

public sealed class MetricsSummaryFaker : Faker<MetricsSummary>
{
    private static readonly string[] Languages = ["Ladder", "ST", "FBD", "SFC", "IL", "LAD", "SCL", "STL", "GRAPH"];
    private static readonly string[] DataTypes = ["BOOL", "INT", "DINT", "SINT", "REAL", "STRING", "TIMER", "COUNTER", "DWORD", "WORD", "BYTE", "ARRAY", "STRUCT", "UDT"];

    public MetricsSummaryFaker()
    {
        CustomInstantiator(f =>
        {
            var totalPrograms = f.Random.Int(1, 50);
            var totalRoutines = f.Random.Int(totalPrograms, totalPrograms * 10);
            var totalRungs = f.Random.Int(totalRoutines * 5, totalRoutines * 50);
            var totalBlocks = f.Random.Int(0, 100);
            var totalNetworks = totalBlocks > 0 ? f.Random.Int(totalBlocks * 2, totalBlocks * 20) : 0;
            var totalTags = f.Random.Int(50, 1000);
            var globalTags = f.Random.Int(10, totalTags / 2);
            var localTags = f.Random.Int(10, totalTags - globalTags);
            var ioTags = totalTags - globalTags - localTags;
            var totalIoModules = f.Random.Int(1, 30);
            var inputModules = f.Random.Int(0, totalIoModules);
            var outputModules = f.Random.Int(0, totalIoModules - inputModules);

            return new MetricsSummary
            {
                TotalPrograms = totalPrograms,
                TotalRoutines = totalRoutines,
                TotalRungs = totalRungs,
                TotalBlocks = totalBlocks,
                TotalNetworks = totalNetworks,
                TotalTags = totalTags,
                GlobalTags = globalTags,
                LocalTags = localTags,
                IoTags = ioTags,
                TotalIoModules = totalIoModules,
                InputModules = inputModules,
                OutputModules = outputModules,
                TotalTasks = f.Random.Int(1, 10),
                TotalInstructions = f.Random.Int(100, 10000),
                LanguageDistribution = GenerateLanguageDistribution(f, totalRoutines, totalBlocks),
                DataTypeDistribution = GenerateDataTypeDistribution(f, totalTags),
                ComplexityScore = CalculateComplexityScore(f, totalPrograms, totalRoutines, totalTags),
                TotalMemoryBytes = f.Random.Bool(0.7f) ? f.Random.Long(10000, 10000000) : null,
                AdditionalMetrics = GenerateAdditionalMetrics(f)
            };
        });
    }

    private static Dictionary<string, int> GenerateLanguageDistribution(Faker f, int totalRoutines, int totalBlocks)
    {
        var distribution = new Dictionary<string, int>();
        var total = totalRoutines + totalBlocks;

        if (total == 0) return distribution;

        var languageCount = f.Random.Int(1, Math.Min(4, Languages.Length));
        var selectedLanguages = f.PickRandom(Languages, languageCount).ToList();
        var remaining = total;

        for (int i = 0; i < selectedLanguages.Count; i++)
        {
            if (i == selectedLanguages.Count - 1)
            {
                distribution[selectedLanguages[i]] = remaining;
            }
            else
            {
                var count = f.Random.Int(1, remaining - (selectedLanguages.Count - i - 1));
                distribution[selectedLanguages[i]] = count;
                remaining -= count;
            }
        }

        return distribution;
    }

    private static Dictionary<string, int> GenerateDataTypeDistribution(Faker f, int totalTags)
    {
        var distribution = new Dictionary<string, int>();

        if (totalTags == 0) return distribution;

        var typeCount = f.Random.Int(2, Math.Min(6, DataTypes.Length));
        var selectedTypes = f.PickRandom(DataTypes, typeCount).ToList();
        var remaining = totalTags;

        for (int i = 0; i < selectedTypes.Count; i++)
        {
            if (i == selectedTypes.Count - 1)
            {
                distribution[selectedTypes[i]] = remaining;
            }
            else
            {
                var count = f.Random.Int(1, remaining - (selectedTypes.Count - i - 1));
                distribution[selectedTypes[i]] = count;
                remaining -= count;
            }
        }

        return distribution;
    }

    private static double CalculateComplexityScore(Faker f, int programs, int routines, int tags)
    {
        // Simple complexity calculation based on project size
        var baseScore = Math.Min(100, (programs * 2) + (routines * 0.5) + (tags * 0.1));

        // Add some randomness
        var variance = f.Random.Double(-10, 10);
        var finalScore = Math.Max(0, Math.Min(100, baseScore + variance));

        return Math.Round(finalScore, 2);
    }

    private static Dictionary<string, object> GenerateAdditionalMetrics(Faker f)
    {
        var metrics = new Dictionary<string, object>();

        if (f.Random.Bool(0.5f))
        {
            metrics["AverageRungComplexity"] = f.Random.Double(1.0, 5.0).ToString("F2");
        }

        if (f.Random.Bool(0.4f))
        {
            metrics["UnusedTagsCount"] = f.Random.Int(0, 50);
        }

        if (f.Random.Bool(0.3f))
        {
            metrics["DuplicateLogicDetected"] = f.Random.Bool();
        }

        if (f.Random.Bool(0.3f))
        {
            metrics["MaxNestingLevel"] = f.Random.Int(1, 10);
        }

        if (f.Random.Bool(0.3f))
        {
            metrics["CyclomaticComplexity"] = f.Random.Int(1, 100);
        }

        if (f.Random.Bool(0.2f))
        {
            metrics["CommentRatio"] = f.Random.Double(0.0, 1.0).ToString("F2");
        }

        if (f.Random.Bool(0.2f))
        {
            metrics["EstimatedScanTime"] = $"{f.Random.Double(1.0, 100.0):F2}ms";
        }

        return metrics;
    }
}