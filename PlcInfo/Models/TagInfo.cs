namespace PlcInfo.Models;

/// <summary>
/// Information about a PLC tag/variable.
/// </summary>
public record TagInfo
{
    /// <summary>
    /// Name of the tag.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Data type of the tag.
    /// </summary>
    public string DataType { get; set; } = string.Empty;

    /// <summary>
    /// Scope of the tag (e.g., Global, Local, Program).
    /// </summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>
    /// Usage type (e.g., Input, Output, Local, Public).
    /// </summary>
    public string? Usage { get; set; }

    /// <summary>
    /// Description or comment.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Initial value if specified.
    /// </summary>
    public string? InitialValue { get; set; }

    /// <summary>
    /// Memory address if applicable.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Whether this is an I/O tag.
    /// </summary>
    public bool IsIoTag { get; set; }

    /// <summary>
    /// Associated I/O module name.
    /// </summary>
    public string? IoModule { get; set; }

    /// <summary>
    /// Array dimensions if this is an array.
    /// </summary>
    public int[]? ArrayDimensions { get; set; }

    /// <summary>
    /// For alias tags, the base tag name.
    /// </summary>
    public string? AliasFor { get; set; }

    /// <summary>
    /// Additional tag properties.
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = new();
}