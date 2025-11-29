namespace PlcInfo.Models;

/// <summary>
/// Information about an I/O module.
/// </summary>
public record IoModuleInfo
{
    /// <summary>
    /// Name of the module.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Type of the module (e.g., Digital Input, Analog Output).
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Catalog number/part number.
    /// </summary>
    public string? CatalogNumber { get; set; }

    /// <summary>
    /// Vendor of the module.
    /// </summary>
    public string? Vendor { get; set; }

    /// <summary>
    /// Rack/slot location.
    /// </summary>
    public string? Location { get; set; }

    /// <summary>
    /// Number of channels/points.
    /// </summary>
    public int ChannelCount { get; set; }

    /// <summary>
    /// Communication format.
    /// </summary>
    public string? CommFormat { get; set; }

    /// <summary>
    /// Parent module name (for sub-modules).
    /// </summary>
    public string? ParentModule { get; set; }

    /// <summary>
    /// Configuration data.
    /// </summary>
    public Dictionary<string, string> Configuration { get; set; } = new();

    /// <summary>
    /// Tags associated with this module.
    /// </summary>
    public IReadOnlyList<string> AssociatedTags { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Additional module properties.
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = new();
}