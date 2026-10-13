namespace PlcInfo.Models;

/// <summary>
/// A network (Siemens LAD/FBD/STL) or graphical rung (PLCopen LD) inside a block or routine.
/// </summary>
public record NetworkInfo
{
    /// <summary>
    /// 1-based network number in display order.
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    /// Network title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Network comment.
    /// </summary>
    public string? Comment { get; set; }

    /// <summary>
    /// Language of the network (LAD, FBD, STL, SCL, LD, ...).
    /// </summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// Canonical, vendor-neutral text form of the network logic. Graphical logic is
    /// serialized deterministically so two versions can be compared line by line.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// Instructions (contacts, coils, boxes, calls) found in the network.
    /// </summary>
    public IReadOnlyList<InstructionInfo> Instructions { get; set; } = Array.Empty<InstructionInfo>();

    /// <summary>
    /// Additional properties.
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = new();
}
