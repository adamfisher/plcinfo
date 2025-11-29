namespace PlcInfo.Models;

/// <summary>
/// Information about a PLC task.
/// </summary>
public record TaskInfo
{
    /// <summary>
    /// Name of the task.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Type of the task (e.g., Continuous, Periodic, Event).
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Priority of the task.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Rate/period in milliseconds for periodic tasks.
    /// </summary>
    public double? Rate { get; set; }

    /// <summary>
    /// Whether the task is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Programs associated with this task.
    /// </summary>
    public IReadOnlyList<string> ProgramNames { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Watchdog timeout in milliseconds.
    /// </summary>
    public double? WatchdogTimeout { get; set; }

    /// <summary>
    /// Additional task properties.
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = new();
}