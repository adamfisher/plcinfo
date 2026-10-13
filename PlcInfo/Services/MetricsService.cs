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
        // Program-scoped tags live on ProgramInfo.LocalTags, not PlcProject.Tags.
        var allTags = AllTags(project);
        var languageDistribution = CalculateLanguageDistribution(project);

        var metrics = new MetricsSummary
        {
            TotalPrograms = project.Programs.Count,
            TotalRoutines = project.Programs.Sum(p => p.Routines.Count),
            TotalRungs = project.Programs.SelectMany(p => p.Routines).Sum(r => r.RungCount),
            TotalBlocks = project.Blocks.Count,
            TotalNetworks = project.Blocks.Sum(b => b.NetworkCount),
            TotalTags = allTags.Count,
            GlobalTags = allTags.Count(IsGlobalScope),
            LocalTags = allTags.Count(t => !IsGlobalScope(t)),
            IoTags = allTags.Count(t => t.IsIoTag),
            TotalIoModules = project.IoModules.Count,
            InputModules = CountInputModules(project.IoModules),
            OutputModules = CountOutputModules(project.IoModules),
            TotalTasks = project.Tasks.Count,
            TotalInstructions = CountInstructions(project),
            LanguageDistribution = languageDistribution,
            DataTypeDistribution = CalculateDataTypeDistribution(allTags),
            ComplexityScore = CalculateComplexityScore(project, allTags.Count, languageDistribution.Count),
            TotalMemoryBytes = EstimateMemoryUsage(project, allTags.Count)
        };

        return metrics;
    }

    private static List<TagInfo> AllTags(PlcProject project) =>
        project.Tags.Concat(project.Programs.SelectMany(p => p.LocalTags)).ToList();

    private static bool IsGlobalScope(TagInfo tag) =>
        tag.Scope.Equals("Global", StringComparison.OrdinalIgnoreCase) ||
        tag.Scope.Equals("Controller", StringComparison.OrdinalIgnoreCase);

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

        void Count(string? language)
        {
            if (!string.IsNullOrEmpty(language))
                distribution[language] = distribution.GetValueOrDefault(language) + 1;
        }

        foreach (var program in project.Programs)
        {
            // A program's language is a summary of its routines, so only fall back
            // to it when there are no routines; otherwise it would be counted twice.
            if (program.Routines.Count == 0)
            {
                Count(program.Language);
                continue;
            }

            foreach (var routine in program.Routines)
                Count(routine.Language);
        }

        foreach (var block in project.Blocks)
            Count(block.Language);

        return distribution;
    }

    private Dictionary<string, int> CalculateDataTypeDistribution(IReadOnlyList<TagInfo> tags)
    {
        var distribution = new Dictionary<string, int>();

        foreach (var tag in tags)
        {
            var dataType = NormalizeDataType(tag.DataType);
            distribution[dataType] = distribution.GetValueOrDefault(dataType) + 1;
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
            // Search after the bounds so names like MAX_OFFSET inside [...] don't match.
            var boundsEnd = dataType.LastIndexOf(']');
            var ofIndex = dataType.IndexOf(" OF ", Math.Max(boundsEnd, 0), StringComparison.OrdinalIgnoreCase);
            if (ofIndex > 0)
            {
                return "ARRAY OF " + NormalizeDataType(dataType[(ofIndex + 4)..].Trim());
            }
            return "ARRAY";
        }

        // Common normalizations
        var normalized = dataType.ToUpperInvariant();
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

    private double CalculateComplexityScore(PlcProject project, int tagCount, int languageCount)
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
        score += Math.Min(tagCount / 100.0, 10);

        // Language diversity
        score += Math.Min(languageCount * 2, 10);

        return Math.Min(score, 100);
    }

    private long? EstimateMemoryUsage(PlcProject project, int tagCount)
    {
        long totalBytes = 0;

        // Estimate based on tags (assume average 4 bytes per tag)
        totalBytes += tagCount * 4L;

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