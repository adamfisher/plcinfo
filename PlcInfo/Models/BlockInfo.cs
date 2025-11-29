namespace PlcInfo.Models;

/// <summary>
/// Information about a Siemens block (OB, FB, FC, DB).
/// </summary>
public record BlockInfo
{
    /// <summary>
    /// Block name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Block number.
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    /// Block type (OB, FB, FC, DB).
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Programming language (e.g., LAD, FBD, STL, SCL).
    /// </summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// Block title/description.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Block comment.
    /// </summary>
    public string? Comment { get; set; }

    /// <summary>
    /// Number of networks in the block.
    /// </summary>
    public int NetworkCount { get; set; }

    /// <summary>
    /// Interface definition (for FBs).
    /// </summary>
    public Dictionary<string, string> Interface { get; set; } = new();

    /// <summary>
    /// Size in bytes.
    /// </summary>
    public long? SizeBytes { get; set; }

    /// <summary>
    /// Additional block properties.
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = new();
}