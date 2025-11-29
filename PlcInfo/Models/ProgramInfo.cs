namespace PlcInfo.Models;

/// <summary>
/// Information about a PLC program.
/// </summary>
public record ProgramInfo
{
    /// <summary>
    /// Name of the program.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Type of the program (e.g., Program, Function Block).
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Programming language (e.g., Ladder, ST, FBD).
    /// </summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// Description or comment.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Parent task name.
    /// </summary>
    public string? TaskName { get; set; }

    /// <summary>
    /// Routines within the program.
    /// </summary>
    public IReadOnlyList<RoutineInfo> Routines { get; set; } = Array.Empty<RoutineInfo>();

    /// <summary>
    /// Local tags/variables.
    /// </summary>
    public IReadOnlyList<TagInfo> LocalTags { get; set; } = Array.Empty<TagInfo>();

    /// <summary>
    /// Size in bytes if available.
    /// </summary>
    public long? SizeBytes { get; set; }

    /// <summary>
    /// Additional program properties.
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = new();
}