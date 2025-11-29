namespace PlcInfo.Models;

/// <summary>
/// Information about a ladder logic rung.
/// </summary>
public record RungInfo
{
    /// <summary>
    /// Rung number.
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    /// Rung comment/description.
    /// </summary>
    public string? Comment { get; set; }

    /// <summary>
    /// Instructions on the rung.
    /// </summary>
    public IReadOnlyList<InstructionInfo> Instructions { get; set; } = Array.Empty<InstructionInfo>();

    /// <summary>
    /// Raw text representation of the rung.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// Additional rung properties.
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = new();
}