// The record layouts implemented here were learned from hutcheb/acd
// (https://github.com/hutcheb/acd: resources/templates/Dat/Dat.ksy,
// resources/templates/Comps/*.ksy, resources/templates/SbRegion/FAFA_SbRegion.ksy,
// acd/record/comps.py, acd/record/sbregion.py, acd/l5x/export_l5x.py and
// acd/l5x/elements.py), Copyright the hutcheb/acd contributors, licensed under the
// Apache License, Version 2.0. This is an independent, reduced C# port of that logic;
// see PlcInfo.Tests/Fixtures/THIRD_PARTY_NOTICES.md.
// Rockwell publishes no specification for these databases; everything here is unofficial.

using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace PlcInfo.Parsing;

/// <summary>
/// One component record from <c>Comps.Dat</c>: a node in the project's object tree.
/// </summary>
/// <param name="ObjectId">Unique object id.</param>
/// <param name="ParentId">Object id of the parent node (0 for roots).</param>
/// <param name="Name">Component name (e.g. a program, routine or collection name).</param>
/// <param name="SequenceNumber">Order of the component within its parent.</param>
/// <param name="RecordType">Record type (256 for named components such as programs, routines, tasks).</param>
/// <param name="Body">The record payload (an "Rx" object record).</param>
/// <param name="RecordIndex">Position of the record in Comps.Dat; breaks sequence-number ties.</param>
internal sealed record AcdComponent(uint ObjectId, uint ParentId, string Name, int SequenceNumber, int RecordType, byte[] Body, int RecordIndex = 0);

/// <summary>
/// The parsed header and attribute list of an "Rx" object record (the body of a component).
/// </summary>
internal sealed class AcdRxRecord
{
    /// <summary>CIP class of the object (e.g. 0x68/0x6B tags, 0x69 modules, 0x6C data types).</summary>
    public int CipType { get; init; }

    /// <summary>Id used to link the object to comments and to task schedules.</summary>
    public int CommentId { get; init; }

    /// <summary>Attributes by id.</summary>
    public Dictionary<uint, byte[]> Attributes { get; } = new();

    /// <summary>
    /// Parses the generic Rx layout: parent id (u32), unique id (u32), format version (u16),
    /// CIP type (u16), comment id (u16), a 0x3C-byte main record, then a length (u32),
    /// an attribute count (u32) and (id u32, length u32, value) attribute records.
    /// Returns null when the body does not have this layout.
    /// </summary>
    public static AcdRxRecord? TryParse(byte[] body)
    {
        if (body.Length < 0x52)
            return null;

        var record = new AcdRxRecord
        {
            CipType = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(0x0A)),
            CommentId = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(0x0C)),
        };

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0x4E));
        int offset = 0x52;
        for (uint i = 0; i < count && offset + 8 <= body.Length; i++)
        {
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(offset));
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(offset + 4));
            // The last attribute's stored length can run 4 bytes past the record (hutcheb/acd
            // "last_attribute_record"); clamp instead of failing.
            int available = body.Length - offset - 8;
            int take = (int)Math.Min(length, (uint)available);
            record.Attributes.TryAdd(id, body.AsSpan(offset + 8, take).ToArray());
            offset += 8 + take;
        }
        return record;
    }

    /// <summary>Reads a u32 from the start of an attribute, or null if it is absent or short.</summary>
    public uint? AttributeUInt32(uint id, int at = 0) =>
        Attributes.TryGetValue(id, out var value) && value.Length >= at + 4
            ? BinaryPrimitives.ReadUInt32LittleEndian(value.AsSpan(at))
            : null;
}

/// <summary>
/// Reads the binary databases inside an ACD: <c>Comps.Dat</c> (the component tree) and
/// <c>SbRegion.Dat</c> (ladder rung text), and links rungs to routines via the
/// "Region Map" component.
/// </summary>
internal sealed class RockwellAcdDatabase
{
    private const ushort FafaRecord = 0xFAFA;
    private const ushort FdfdRecord = 0xFDFD;
    private const ushort FefeRecord = 0xFEFE;
    private const ushort BffbRecord = 0xFBBF;

    private static readonly Regex AtReference = new("@([0-9A-Fa-f]+)@", RegexOptions.CultureInvariant);
    private static readonly Regex AmpReference = new("&([0-9a-f]{8}):", RegexOptions.CultureInvariant);

    private readonly Dictionary<uint, List<AcdComponent>> _children = new();
    private readonly Dictionary<uint, List<(uint RungId, uint Order)>> _regionsByParent = new();
    private readonly Dictionary<uint, string> _rungText = new();

    private RockwellAcdDatabase(Dictionary<uint, AcdComponent> components)
    {
        Components = components;
        foreach (var component in components.Values)
        {
            if (!_children.TryGetValue(component.ParentId, out var list))
                _children[component.ParentId] = list = new List<AcdComponent>();
            list.Add(component);
        }
        foreach (var list in _children.Values)
            list.Sort((a, b) => a.SequenceNumber != b.SequenceNumber
                ? a.SequenceNumber.CompareTo(b.SequenceNumber)
                : a.RecordIndex.CompareTo(b.RecordIndex));
    }

    /// <summary>All components by object id.</summary>
    public IReadOnlyDictionary<uint, AcdComponent> Components { get; }

    /// <summary>Problems found while decoding individual records.</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>
    /// Decodes <c>Comps.Dat</c> and, if given, <c>SbRegion.Dat</c>.
    /// Throws <see cref="InvalidDataException"/> if the component database has an unknown layout.
    /// </summary>
    public static RockwellAcdDatabase Load(byte[] compsDat, byte[]? sbRegionDat)
    {
        var components = new Dictionary<uint, AcdComponent>();
        int index = 0;
        foreach (var (identifier, payload) in ReadRecords(compsDat, "Comps.Dat"))
        {
            var component = ParseComponent(identifier, payload);
            if (component is null)
                continue;
            component = component with { RecordIndex = index++ };
            // Some objects appear more than once; the largest record is the complete one
            // (hutcheb/acd export_l5x.py).
            if (!components.TryGetValue(component.ObjectId, out var existing) || component.Body.Length > existing.Body.Length)
                components[component.ObjectId] = component;
        }

        var database = new RockwellAcdDatabase(components);
        database.LoadRegionMap();
        if (sbRegionDat is not null)
            database.LoadRungText(sbRegionDat);
        return database;
    }

    /// <summary>Children of a component, in sequence order.</summary>
    public IReadOnlyList<AcdComponent> Children(uint parentId) =>
        _children.TryGetValue(parentId, out var list) ? list : Array.Empty<AcdComponent>();

    /// <summary>The first child with the given name, or null.</summary>
    public AcdComponent? Child(uint parentId, string name) =>
        Children(parentId).FirstOrDefault(c => c.Name == name);

    /// <summary>Name of the component with this id, or null.</summary>
    public string? NameOf(uint objectId) =>
        Components.TryGetValue(objectId, out var component) ? component.Name : null;

    /// <summary>
    /// Returns the ladder rung texts of a routine in rung order, with object references
    /// replaced by component names. Non-ladder regions (e.g. compiled FBD) are not included.
    /// </summary>
    public IReadOnlyList<string> RungsOf(uint routineId)
    {
        if (!_regionsByParent.TryGetValue(routineId, out var regions))
            return Array.Empty<string>();

        return regions
            .OrderBy(r => r.Order)
            .Where(r => _rungText.ContainsKey(r.RungId))
            .Select(r => ResolveReferences(_rungText[r.RungId]))
            .ToList();
    }

    /// <summary>
    /// Reads the records of a .Dat database: a header whose u32 at offset 8 is the end of the
    /// record area and whose u32 at offset 12 is the first record position, then records of
    /// (identifier u16, length u32 including these 6 bytes, payload).
    /// </summary>
    private static IEnumerable<(ushort Identifier, byte[] Payload)> ReadRecords(byte[] dat, string name)
    {
        if (dat.Length < 16)
            throw new InvalidDataException($"{name} is too small.");

        long end = Math.Min((long)BinaryPrimitives.ReadUInt32LittleEndian(dat.AsSpan(8)) + 1, dat.Length);
        long position = BinaryPrimitives.ReadUInt32LittleEndian(dat.AsSpan(12));
        if (position < 16 || position >= dat.Length)
            throw new InvalidDataException($"{name} has an invalid record area.");

        while (position + 6 <= end)
        {
            ushort identifier = BinaryPrimitives.ReadUInt16LittleEndian(dat.AsSpan((int)position));
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(dat.AsSpan((int)position + 2));
            if (identifier is not (FafaRecord or FdfdRecord or FefeRecord or BffbRecord) || length < 6 || position + length > dat.Length)
                throw new InvalidDataException($"{name} has an unknown record at offset {position}.");

            yield return (identifier, dat.AsSpan((int)position + 6, (int)length - 6).ToArray());
            position += length;
        }
    }

    /// <summary>
    /// Component record headers (hutcheb/acd FAFA_Comps.ksy / FDFD_Comps.ksy):
    /// FAFA: record length u32, then a 144-byte header (sequence u16 @4, type u16 @6,
    /// object id u32 @0x0C, parent id u32 @0x10, UTF-16 name @0x14), then the body.
    /// FDFD: a 155-byte header (sequence u16 @4, type u16 @0x0A, object id u32 @0x10,
    /// parent id u32 @0x14, UTF-16 name @0x18), then the body (minus an 8-byte trailer).
    /// </summary>
    private static AcdComponent? ParseComponent(ushort identifier, byte[] raw)
    {
        if (identifier == FafaRecord)
        {
            if (raw.Length < 148)
                return null;
            // v20 files use an older layout with a non-zero value here (hutcheb/acd comps.py);
            // it is not supported.
            if (BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(0x0C)) != 0)
                throw new InvalidDataException("Comps.Dat uses a legacy record layout that is not supported.");

            int recordLength = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(raw), (uint)raw.Length);
            var header = raw.AsSpan(4, 144);
            return new AcdComponent(
                BinaryPrimitives.ReadUInt32LittleEndian(header[0x0C..]),
                BinaryPrimitives.ReadUInt32LittleEndian(header[0x10..]),
                ReadUtf16Z(header.Slice(0x14, 124)),
                BinaryPrimitives.ReadUInt16LittleEndian(header[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(header[6..]),
                recordLength > 148 ? raw[148..recordLength] : Array.Empty<byte>());
        }

        if (identifier == FdfdRecord)
        {
            if (raw.Length < 155)
                return null;
            var header = raw.AsSpan(0, 155);
            int bodyEnd = Math.Max(155, raw.Length - 2); // physical length - 6 header - 2 = len_record - 8
            return new AcdComponent(
                BinaryPrimitives.ReadUInt32LittleEndian(header[0x10..]),
                BinaryPrimitives.ReadUInt32LittleEndian(header[0x14..]),
                ReadUtf16Z(header.Slice(0x18, 124)),
                BinaryPrimitives.ReadUInt16LittleEndian(header[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(header[0x0A..]),
                raw[155..bodyEnd]);
        }

        return null;
    }

    /// <summary>
    /// The root "Region Map" component lists 16-byte entries
    /// (parent routine id, order, sequence, region id) that place each SbRegion record
    /// in a routine (hutcheb/acd export_l5x.py _parse_region_map).
    /// </summary>
    private void LoadRegionMap()
    {
        var map = Children(0).FirstOrDefault(c => c.Name == "Region Map");
        if (map is null)
            return;

        var record = map.Body;
        if (record.Length == 0x4A)
            return; // Empty project: header only.

        int start;
        int length;
        if (record.Length >= 0x4E && BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(0x4A)) == record.Length - 0x4A)
        {
            start = 0x4E;
            length = (int)BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(0x4A)) - 4;
        }
        else if (record.Length >= 0x1C && BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(0x18)) is var legacy
                 && (legacy == record.Length - 0x1C || legacy == record.Length - 0x18))
        {
            start = 0x1C;
            length = record.Length - start;
        }
        else
        {
            Warnings.Add("The ACD region map has an unknown layout; ladder rungs were not decoded.");
            return;
        }

        int end = start + length - (length % 16);
        for (int offset = start; offset + 16 <= end; offset += 16)
        {
            uint parent = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(offset));
            uint order = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(offset + 4));
            uint regionId = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(offset + 12));
            if (!_regionsByParent.TryGetValue(parent, out var list))
                _regionsByParent[parent] = list = new List<(uint, uint)>();
            list.Add((regionId, order));
        }
    }

    /// <summary>
    /// SbRegion FAFA records (hutcheb/acd FAFA_SbRegion.ksy): record length u32, u16,
    /// region id u32, a 41-byte NUL-padded language type, text length u32, UTF-16LE text.
    /// Only "Rung NT" regions hold ladder rung text; "REGION NT" holds compiled
    /// function-block logic, which is not routine source and is ignored.
    /// </summary>
    private void LoadRungText(byte[] sbRegionDat)
    {
        foreach (var (identifier, raw) in ReadRecords(sbRegionDat, "SbRegion.Dat"))
        {
            if (identifier != FafaRecord || raw.Length < 55)
                continue;

            uint regionId = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(6));
            var languageField = raw.AsSpan(10, 41);
            int nul = languageField.IndexOf((byte)0);
            var language = Encoding.ASCII.GetString(nul >= 0 ? languageField[..nul] : languageField);
            if (language != "Rung NT")
                continue;

            int textLength = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(51)), (uint)(raw.Length - 55));
            _rungText[regionId] = Encoding.Unicode.GetString(raw, 55, textLength & ~1).TrimEnd('\0');
        }
    }

    /// <summary>
    /// Rung text refers to tags and other objects as <c>@hexid@</c> (and some as
    /// <c>&amp;hexid:</c>); replace them with component names.
    /// </summary>
    private string ResolveReferences(string text)
    {
        var resolved = AtReference.Replace(text, m => NameFor(m.Value, m.Groups[1].Value, "") ?? m.Value);
        return AmpReference.Replace(resolved, m => NameFor(m.Value, m.Groups[1].Value, ":") ?? m.Value);
    }

    private string? NameFor(string token, string hex, string suffix)
    {
        if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var id)
            && NameOf(id) is { } name)
            return name + suffix;

        var warning = $"Rung reference {token} could not be resolved to a name.";
        if (!Warnings.Contains(warning))
            Warnings.Add(warning);
        return null;
    }

    private static string ReadUtf16Z(ReadOnlySpan<byte> field)
    {
        int end = 0;
        while (end + 1 < field.Length && (field[end] != 0 || field[end + 1] != 0))
            end += 2;
        return Encoding.Unicode.GetString(field[..end]);
    }
}
