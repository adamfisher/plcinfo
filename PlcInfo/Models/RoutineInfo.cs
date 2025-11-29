namespace PlcInfo.Models;

/// <summary>
/// Information about a PLC routine.
/// </summary>
public record RoutineInfo
{
    /// <summary>
    /// Name of the routine.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Type of the routine (e.g., Main, Subroutine).
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Programming language (e.g., Ladder, ST, FBD).
    /// </summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// Parent program name.
    /// </summary>
    public string? ProgramName { get; set; }

    /// <summary>
    /// Description or comment.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Number of rungs (for ladder logic).
    /// </summary>
    public int RungCount { get; set; }

    /// <summary>
    /// Number of networks (for FBD/LAD in Siemens).
    /// </summary>
    public int NetworkCount { get; set; }

    /// <summary>
    /// List of rungs in the routine.
    /// </summary>
    public IReadOnlyList<RungInfo> Rungs { get; set; } = Array.Empty<RungInfo>();

    /// <summary>
    /// Size in bytes if available.
    /// </summary>
    public long? SizeBytes { get; set; }

    /// <summary>
    /// Additional routine properties.
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = new();
}