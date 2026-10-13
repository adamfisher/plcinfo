namespace PlcInfo.Models;

/// <summary>
/// Represents a complete PLC project with all its components.
/// </summary>
public record PlcProject
{
    /// <summary>
    /// The vendor/manufacturer of the PLC.
    /// </summary>
    public string Vendor { get; set; } = string.Empty;

    /// <summary>
    /// The specific format of the project file.
    /// </summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>
    /// The full path to the project file.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Information about the controller/PLC.
    /// </summary>
    public ControllerInfo? Controller { get; set; }

    /// <summary>
    /// List of tasks in the project.
    /// </summary>
    public IReadOnlyList<TaskInfo> Tasks { get; set; } = Array.Empty<TaskInfo>();

    /// <summary>
    /// List of programs in the project.
    /// </summary>
    public IReadOnlyList<ProgramInfo> Programs { get; set; } = Array.Empty<ProgramInfo>();

    /// <summary>
    /// List of blocks (for Siemens projects).
    /// </summary>
    public IReadOnlyList<BlockInfo> Blocks { get; set; } = Array.Empty<BlockInfo>();

    /// <summary>
    /// List of tags/variables in the project.
    /// </summary>
    public IReadOnlyList<TagInfo> Tags { get; set; } = Array.Empty<TagInfo>();

    /// <summary>
    /// List of I/O modules in the project.
    /// </summary>
    public IReadOnlyList<IoModuleInfo> IoModules { get; set; } = Array.Empty<IoModuleInfo>();

    /// <summary>
    /// User-defined data types (UDTs, DUTs, PLC data types).
    /// </summary>
    public IReadOnlyList<DataTypeInfo> DataTypes { get; set; } = Array.Empty<DataTypeInfo>();

    /// <summary>
    /// Timestamp when the project was last modified.
    /// </summary>
    public DateTime? LastModified { get; set; }

    /// <summary>
    /// Project metadata.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}