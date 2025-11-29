namespace PlcInfo.Models;

/// <summary>
/// Information about a PLC instruction.
/// </summary>
public record InstructionInfo
{
    /// <summary>
    /// Instruction mnemonic (e.g., XIC, XIO, OTE).
    /// </summary>
    public string Mnemonic { get; set; } = string.Empty;

    /// <summary>
    /// Instruction type (e.g., Input, Output, Function).
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Operands/parameters for the instruction.
    /// </summary>
    public IReadOnlyList<string> Operands { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Position in the rung.
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// Additional instruction properties.
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = new();
}