namespace PlcInfo.Models;

/// <summary>
/// Summary of project metrics.
/// </summary>
public record MetricsSummary
{
    /// <summary>
    /// Total number of programs.
    /// </summary>
    public int TotalPrograms { get; set; }

    /// <summary>
    /// Total number of routines.
    /// </summary>
    public int TotalRoutines { get; set; }

    /// <summary>
    /// Total number of rungs.
    /// </summary>
    public int TotalRungs { get; set; }

    /// <summary>
    /// Total number of blocks (Siemens).
    /// </summary>
    public int TotalBlocks { get; set; }

    /// <summary>
    /// Total number of networks (Siemens).
    /// </summary>
    public int TotalNetworks { get; set; }

    /// <summary>
    /// Total number of tags.
    /// </summary>
    public int TotalTags { get; set; }

    /// <summary>
    /// Number of global tags.
    /// </summary>
    public int GlobalTags { get; set; }

    /// <summary>
    /// Number of local tags.
    /// </summary>
    public int LocalTags { get; set; }

    /// <summary>
    /// Number of I/O tags.
    /// </summary>
    public int IoTags { get; set; }

    /// <summary>
    /// Total number of I/O modules.
    /// </summary>
    public int TotalIoModules { get; set; }

    /// <summary>
    /// Number of input modules.
    /// </summary>
    public int InputModules { get; set; }

    /// <summary>
    /// Number of output modules.
    /// </summary>
    public int OutputModules { get; set; }

    /// <summary>
    /// Total number of tasks.
    /// </summary>
    public int TotalTasks { get; set; }

    /// <summary>
    /// Total instructions count.
    /// </summary>
    public int TotalInstructions { get; set; }

    /// <summary>
    /// Language distribution (language -> count).
    /// </summary>
    public Dictionary<string, int> LanguageDistribution { get; set; } = new();

    /// <summary>
    /// Data type distribution (type -> count).
    /// </summary>
    public Dictionary<string, int> DataTypeDistribution { get; set; } = new();

    /// <summary>
    /// Complexity score (0-100).
    /// </summary>
    public double ComplexityScore { get; set; }

    /// <summary>
    /// Total memory usage in bytes.
    /// </summary>
    public long? TotalMemoryBytes { get; set; }

    /// <summary>
    /// Additional metrics.
    /// </summary>
    public Dictionary<string, object> AdditionalMetrics { get; set; } = new();
}