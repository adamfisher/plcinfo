namespace PlcInfo.Models;

/// <summary>
/// A user-defined data type: a Rockwell UDT, a Siemens PLC data type (UDT),
/// or an IEC 61131-3 DUT (STRUCT, enumeration, alias, union).
/// </summary>
public record DataTypeInfo
{
    /// <summary>
    /// Data type name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Kind of type: Struct, Enum, Alias or Union.
    /// </summary>
    public string Kind { get; set; } = "Struct";

    /// <summary>
    /// Data type description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Members (struct fields or enumeration values), in declaration order.
    /// </summary>
    public IReadOnlyList<DataTypeMember> Members { get; set; } = Array.Empty<DataTypeMember>();

    /// <summary>
    /// For an alias type, the type it aliases.
    /// </summary>
    public string? BaseType { get; set; }

    /// <summary>
    /// Additional properties.
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = new();
}

/// <summary>
/// A member of a <see cref="DataTypeInfo"/>.
/// </summary>
public record DataTypeMember
{
    /// <summary>
    /// Member name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Member data type. Empty for enumeration values.
    /// </summary>
    public string DataType { get; set; } = string.Empty;

    /// <summary>
    /// Array dimensions as written in the source (e.g. "10" or "1..3"), if any.
    /// </summary>
    public string? Dimensions { get; set; }

    /// <summary>
    /// Member description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Initial value, or the value of an enumeration member.
    /// </summary>
    public string? InitialValue { get; set; }

    /// <summary>
    /// Additional properties.
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = new();
}
