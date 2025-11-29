using PlcInfo.Models;

namespace PlcInfo.Services;

/// <summary>
/// Service for calculating project metrics.
/// </summary>
public class MetricsService
{
    /// <summary>
    /// Calculates metrics for a PLC project.
    /// </summary>
    public MetricsSummary CalculateMetrics(PlcProject project)
    {
        var metrics = new MetricsSummary
        {
            TotalPrograms = project.Programs.Count,
            TotalRoutines = project.Programs.Sum(p => p.Routines.Count),
            TotalRungs = project.Programs.SelectMany(p => p.Routines).Sum(r => r.RungCount),
            TotalBlocks = project.Blocks.Count,
            TotalNetworks = project.Blocks.Sum(b => b.NetworkCount),
            TotalTags = project.Tags.Count,
            GlobalTags = project.Tags.Count(t => t.Scope.Equals("Global", StringComparison.OrdinalIgnoreCase) ||
                                                  t.Scope.Equals("Controller", StringComparison.OrdinalIgnoreCase)),
            LocalTags = project.Tags.Count(t => !t.Scope.Equals("Global", StringComparison.OrdinalIgnoreCase) &&
                                                 !t.Scope.Equals("Controller", StringComparison.OrdinalIgnoreCase)),
            IoTags = project.Tags.Count(t => t.IsIoTag),
            TotalIoModules = project.IoModules.Count,
            InputModules = CountInputModules(project.IoModules),
            OutputModules = CountOutputModules(project.IoModules),
            TotalTasks = project.Tasks.Count,
            TotalInstructions = CountInstructions(project),
            LanguageDistribution = CalculateLanguageDistribution(project),
            DataTypeDistribution = CalculateDataTypeDistribution(project),
            ComplexityScore = CalculateComplexityScore(project),
            TotalMemoryBytes = EstimateMemoryUsage(project)
        };

        return metrics;
    }

    private int CountInputModules(IReadOnlyList<IoModuleInfo> modules)
    {
        return modules.Count(m =>
            m.Type.Contains("Input", StringComparison.OrdinalIgnoreCase) &&
            !m.Type.Contains("Output", StringComparison.OrdinalIgnoreCase));
    }

    private int CountOutputModules(IReadOnlyList<IoModuleInfo> modules)
    {
        return modules.Count(m =>
            m.Type.Contains("Output", StringComparison.OrdinalIgnoreCase) &&
            !m.Type.Contains("Input", StringComparison.OrdinalIgnoreCase));
    }

    private int CountInstructions(PlcProject project)
    {
        return project.Programs
            .SelectMany(p => p.Routines)
            .SelectMany(r => r.Rungs)
            .Sum(r => r.Instructions.Count);
    }

    private Dictionary<string, int> CalculateLanguageDistribution(PlcProject project)
    {
        var distribution = new Dictionary<string, int>();

        // Count from programs
        foreach (var program in project.Programs)
        {
            if (!string.IsNullOrEmpty(program.Language))
            {
                if (distribution.ContainsKey(program.Language))
                    distribution[program.Language]++;
                else
                    distribution[program.Language] = 1;
            }

            // Count from routines
            foreach (var routine in program.Routines)
            {
                if (!string.IsNullOrEmpty(routine.Language))
                {
                    if (distribution.ContainsKey(routine.Language))
                        distribution[routine.Language]++;
                    else
                        distribution[routine.Language] = 1;
                }
            }
        }

        // Count from blocks
        foreach (var block in project.Blocks)
        {
            if (!string.IsNullOrEmpty(block.Language))
            {
                if (distribution.ContainsKey(block.Language))
                    distribution[block.Language]++;
                else
                    distribution[block.Language] = 1;
            }
        }

        return distribution;
    }

    private Dictionary<string, int> CalculateDataTypeDistribution(PlcProject project)
    {
        var distribution = new Dictionary<string, int>();

        foreach (var tag in project.Tags)
        {
            var dataType = NormalizeDataType(tag.DataType);
            if (distribution.ContainsKey(dataType))
                distribution[dataType]++;
            else
                distribution[dataType] = 1;
        }

        return distribution;
    }

    private string NormalizeDataType(string dataType)
    {
        if (string.IsNullOrEmpty(dataType))
            return "Unknown";

        // Extract base type from complex types
        if (dataType.StartsWith("ARRAY", StringComparison.OrdinalIgnoreCase))
        {
            var ofIndex = dataType.IndexOf("OF", StringComparison.OrdinalIgnoreCase);
            if (ofIndex > 0 && ofIndex + 2 < dataType.Length)
            {
                return "ARRAY OF " + dataType.Substring(ofIndex + 3).Trim();
            }
            return "ARRAY";
        }

        // Common normalizations
        var normalized = dataType.ToUpper();
        normalized = normalized switch
        {
            "BOOLEAN" => "BOOL",
            "INTEGER" => "INT",
            "DOUBLE INTEGER" => "DINT",
            "UNSIGNED INTEGER" => "UINT",
            "UNSIGNED DOUBLE INTEGER" => "UDINT",
            _ => normalized
        };

        return normalized;
    }

    private double CalculateComplexityScore(PlcProject project)
    {
        double score = 0;

        // Base complexity from component counts
        score += Math.Min(project.Programs.Count * 2, 20);
        score += Math.Min(project.Tasks.Count * 3, 15);
        score += Math.Min(project.IoModules.Count * 1.5, 15);

        // Complexity from code size
        var totalRungs = project.Programs.SelectMany(p => p.Routines).Sum(r => r.RungCount);
        score += Math.Min(totalRungs / 100.0, 20);

        var totalNetworks = project.Blocks.Sum(b => b.NetworkCount);
        score += Math.Min(totalNetworks / 50.0, 10);

        // Complexity from tag count
        score += Math.Min(project.Tags.Count / 100.0, 10);

        // Language diversity
        var languages = CalculateLanguageDistribution(project).Keys.Count;
        score += Math.Min(languages * 2, 10);

        return Math.Min(score, 100);
    }

    private long? EstimateMemoryUsage(PlcProject project)
    {
        long totalBytes = 0;

        // Estimate based on tags (assume average 4 bytes per tag)
        totalBytes += project.Tags.Count * 4;

        // Estimate based on programs
        foreach (var program in project.Programs)
        {
            if (program.SizeBytes.HasValue)
                totalBytes += program.SizeBytes.Value;
            else
                totalBytes += 1024; // Default 1KB per program

            // Add routine estimates
            foreach (var routine in program.Routines)
            {
                if (routine.SizeBytes.HasValue)
                    totalBytes += routine.SizeBytes.Value;
                else
                    totalBytes += routine.RungCount * 64; // Estimate 64 bytes per rung
            }
        }

        // Estimate based on blocks
        foreach (var block in project.Blocks)
        {
            if (block.SizeBytes.HasValue)
                totalBytes += block.SizeBytes.Value;
            else
                totalBytes += block.NetworkCount * 128; // Estimate 128 bytes per network
        }

        return totalBytes > 0 ? totalBytes : null;
    }
}