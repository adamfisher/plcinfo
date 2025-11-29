namespace PlcInfo.Models;

/// <summary>
/// Information about a PLC controller.
/// </summary>
public record ControllerInfo
{
    /// <summary>
    /// Name of the controller.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Type/model of the controller.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Vendor of the controller.
    /// </summary>
    public string Vendor { get; set; } = string.Empty;

    /// <summary>
    /// Firmware revision.
    /// </summary>
    public string FirmwareRevision { get; set; } = string.Empty;

    /// <summary>
    /// CPU type for Siemens PLCs.
    /// </summary>
    public string? CpuType { get; set; }

    /// <summary>
    /// Rack number for modular PLCs.
    /// </summary>
    public int? Rack { get; set; }

    /// <summary>
    /// Slot number for modular PLCs.
    /// </summary>
    public int? Slot { get; set; }

    /// <summary>
    /// Network configuration count.
    /// </summary>
    public int NetworkCount { get; set; }

    /// <summary>
    /// Additional controller properties.
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = new();
}