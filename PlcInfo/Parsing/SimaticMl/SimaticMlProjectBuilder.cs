using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PlcInfo.Models;

namespace PlcInfo.Parsing.SimaticMl;

/// <summary>
/// Accumulates SimaticML documents (one engineering object each) into a <see cref="PlcProject"/>.
/// </summary>
internal sealed class SimaticMlProjectBuilder
{
    private static readonly Regex ArrayType = new(
        @"^Array\s*\[(?<dims>[^\]]*)\]\s+of\s+(?<type>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex IoAddress = new(
        // English mnemonics %I/%Q (incl. %IW64, %Q0.0:P). German mnemonics write inputs/outputs
        // as %E/%A; no other area uses those letters, so they are accepted too (UNVERIFIED: no
        // German-mnemonic export was available to confirm the exported form).
        @"^%[IQEA](?:[BWDX]?\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly List<BlockInfo> _blocks = new();
    private readonly List<TagInfo> _tags = new();
    private readonly List<DataTypeInfo> _dataTypes = new();
    private readonly List<string> _warnings = new();
    private readonly SortedSet<string> _versions = new(StringComparer.Ordinal);

    /// <summary>
    /// Number of SimaticML documents added.
    /// </summary>
    internal int DocumentCount { get; private set; }

    /// <summary>
    /// Records a per-item problem; <paramref name="source"/> names the export file in directory mode.
    /// </summary>
    internal void Warn(string? source, string message) =>
        _warnings.Add(source == null ? message : $"{source}: {message}");

    /// <summary>
    /// Adds one export. <paramref name="source"/> is the path relative to the export root
    /// (null for a single-file parse); it is used in warnings and block properties.
    /// </summary>
    internal void Add(XDocument document, string? source, long sizeBytes)
    {
        var root = document.Root;
        if (root == null || root.Name.LocalName != "Document")
        {
            Warn(source, "The file is not a SimaticML document and was skipped.");
            return;
        }

        DocumentCount++;
        var version = (string?)root.Child("Engineering")?.Attribute("version");
        if (!string.IsNullOrEmpty(version))
            _versions.Add(version);

        var objects = root.Elements().Where(e => e.Name.LocalName.StartsWith("SW.", StringComparison.Ordinal)).ToList();
        if (objects.Count == 0)
        {
            Warn(source, "The SimaticML document contains no engineering object.");
            return;
        }

        foreach (var obj in objects)
        {
            switch (obj.Name.LocalName)
            {
                case "SW.Blocks.OB":
                case "SW.Blocks.FB":
                case "SW.Blocks.FC":
                case "SW.Blocks.GlobalDB":
                case "SW.Blocks.InstanceDB":
                case "SW.Blocks.ArrayDB":
                    AddBlock(obj, source, version, sizeBytes);
                    break;
                case "SW.Types.PlcStruct":
                    AddDataType(obj, source);
                    break;
                case "SW.Tags.PlcTagTable":
                    AddTagTable(obj, source);
                    break;
                default:
                    Warn(source, $"{obj.Name.LocalName} exports are not supported and were skipped.");
                    break;
            }
        }
    }

    /// <summary>
    /// Builds the project.
    /// </summary>
    internal PlcProject Build(string filePath, DateTime? lastModified)
    {
        var metadata = new Dictionary<string, string>
        {
            ["ExportFormat"] = "SimaticML",
            ["ExportFiles"] = DocumentCount.ToString(CultureInfo.InvariantCulture)
        };
        if (_versions.Count > 0)
            metadata["TiaVersion"] = string.Join(", ", _versions);
        if (_warnings.Count > 0)
            metadata["Warnings"] = string.Join("\n", _warnings);

        return new PlcProject
        {
            Vendor = "Siemens",
            Format = _versions.Count == 1 ? $"TIA Portal {_versions.Min} (SimaticML)" : "TIA Portal (SimaticML)",
            FilePath = filePath,
            Blocks = _blocks,
            Tags = _tags,
            DataTypes = _dataTypes,
            LastModified = lastModified,
            Metadata = metadata
        };
    }

    // ---- blocks ------------------------------------------------------------------

    private void AddBlock(XElement obj, string? source, string? version, long sizeBytes)
    {
        var kind = obj.Name.LocalName["SW.Blocks.".Length..];
        var name = obj.Attr("Name") ?? "?";
        var block = new BlockInfo
        {
            Name = name,
            Number = int.TryParse(obj.Attr("Number"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0,
            Type = kind is "OB" or "FB" or "FC" ? kind : "DB",
            Language = obj.Attr("ProgrammingLanguage") ?? string.Empty,
            Title = obj.MultilingualText("Title"),
            Comment = obj.MultilingualText("Comment"),
            SizeBytes = sizeBytes
        };

        if (block.Type == "DB")
            block.Properties["DbType"] = kind switch { "GlobalDB" => "Global", "InstanceDB" => "Instance", _ => "Array" };
        foreach (var attribute in obj.Child("AttributeList")?.Elements() ?? Enumerable.Empty<XElement>())
        {
            var key = attribute.Name.LocalName;
            if (key is "Interface" or "Name" or "Number" or "ProgrammingLanguage" || attribute.HasElements)
                continue;
            if (!string.IsNullOrWhiteSpace(attribute.Value))
                block.Properties[key] = attribute.Value.Trim();
        }
        if (source != null)
            block.Properties["File"] = source;
        if (version != null)
            block.Properties["TiaVersion"] = version;

        var sections = obj.Child("AttributeList").Child("Interface").Child("Sections");
        foreach (var section in sections.Children("Section"))
        {
            var sectionName = (string?)section.Attribute("Name") ?? string.Empty;
            foreach (var member in section.Children("Member"))
                block.Interface[$"{sectionName}.{(string?)member.Attribute("Name")}"] = SimaticMlXml.Unquote((string?)member.Attribute("Datatype") ?? string.Empty);
        }

        if (string.Equals(obj.Attr("IsKnowHowProtected"), "true", StringComparison.OrdinalIgnoreCase))
            Warn(source, $"Block {name} is know-how protected, so its logic is not in the export.");

        var networks = new List<NetworkInfo>();
        foreach (var unit in obj.Child("ObjectList").Children("SW.Blocks.CompileUnit"))
            networks.Add(BuildNetwork(unit, networks.Count + 1, name, block, source));

        block.Networks = networks;
        block.NetworkCount = networks.Count;
        if (networks.Count > 0 && networks.All(n => n.Language is "SCL" or "STL"))
            block.Source = string.Join("\n\n", networks.Select(n => n.Text ?? string.Empty));

        _blocks.Add(block);

        if (block.Type == "DB")
        {
            foreach (var section in sections.Children("Section"))
            {
                var usage = (string?)section.Attribute("Name") ?? string.Empty;
                foreach (var flat in Flatten(section.Children("Member"), prefix: null))
                    _tags.Add(ToTag(flat, scope: name, usage));
            }
        }
    }

    private NetworkInfo BuildNetwork(XElement unit, int number, string blockName, BlockInfo block, string? source)
    {
        var network = new NetworkInfo
        {
            Number = number,
            Title = unit.MultilingualText("Title"),
            Comment = unit.MultilingualText("Comment"),
            Language = unit.Attr("ProgrammingLanguage") ?? block.Language,
            Text = string.Empty
        };

        var island = unit.Child("AttributeList").Child("NetworkSource")?.Elements().FirstOrDefault();
        if (island == null)
            return network; // An empty network: TIA exports <NetworkSource /> with no logic.

        switch (island.Name.LocalName)
        {
            case "FlgNet":
                (network.Text, network.Instructions) = FlgNetRenderer.Render(island);
                break;
            case "StructuredText":
                (network.Text, network.Instructions) = SclRenderer.Render(island);
                break;
            case "StatementList":
                (network.Text, network.Instructions) = StlRenderer.Render(island);
                break;
            case "Graph":
                var graph = GraphRenderer.Render(island);
                network.Text = graph.Text;
                network.Instructions = graph.Instructions;
                network.Properties["Steps"] = graph.Steps.ToString(CultureInfo.InvariantCulture);
                network.Properties["Transitions"] = graph.Transitions.ToString(CultureInfo.InvariantCulture);
                block.Properties["GraphSteps"] = graph.Steps.ToString(CultureInfo.InvariantCulture);
                block.Properties["GraphTransitions"] = graph.Transitions.ToString(CultureInfo.InvariantCulture);
                if (graph.HasActions)
                    Warn(source, $"GRAPH block {blockName}: step actions are listed in simplified form.");
                break;
            default:
                network.Text = null;
                Warn(source, $"Block {blockName} network {number}: {island.Name.LocalName} logic is not supported and was skipped.");
                break;
        }

        return network;
    }

    // ---- data types --------------------------------------------------------------

    private void AddDataType(XElement obj, string? source)
    {
        var members = new List<DataTypeMember>();
        var sections = obj.Child("AttributeList").Child("Interface").Child("Sections");
        foreach (var section in sections.Children("Section"))
        {
            foreach (var flat in Flatten(section.Children("Member"), prefix: null))
            {
                var (dataType, dims, _) = SplitDatatype(flat.Datatype);
                var member = new DataTypeMember
                {
                    Name = flat.Path,
                    DataType = dataType,
                    Dimensions = dims,
                    Description = flat.Description,
                    InitialValue = flat.StartValue
                };
                if (flat.Element.Attribute("Remanence") is { } remanence)
                    member.Properties["Remanence"] = remanence.Value;
                members.Add(member);
            }
        }

        var dataTypeInfo = new DataTypeInfo
        {
            Name = obj.Attr("Name") ?? "?",
            Kind = "Struct",
            Description = obj.MultilingualText("Comment") ?? obj.MultilingualText("Title"),
            Members = members
        };
        if (source != null)
            dataTypeInfo.Properties["File"] = source;
        _dataTypes.Add(dataTypeInfo);
    }

    // ---- tag tables --------------------------------------------------------------

    private void AddTagTable(XElement obj, string? source)
    {
        var table = obj.Attr("Name") ?? "?";
        foreach (var item in obj.Child("ObjectList")?.Elements() ?? Enumerable.Empty<XElement>())
        {
            var kind = item.Name.LocalName;
            if (kind is not ("SW.Tags.PlcTag" or "SW.Tags.PlcUserConstant"))
                continue;

            var dataType = SimaticMlXml.Unquote(item.Attr("DataTypeName") ?? string.Empty);
            var address = item.Attr("LogicalAddress");
            var tag = new TagInfo
            {
                Name = item.Attr("Name") ?? "?",
                DataType = dataType,
                Scope = "Global",
                Usage = kind == "SW.Tags.PlcUserConstant" ? "Constant" : null,
                Description = item.MultilingualText("Comment"),
                Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim(),
                InitialValue = kind == "SW.Tags.PlcUserConstant" ? item.Attr("Value") : null
            };
            tag.IsIoTag = tag.Address != null && IoAddress.IsMatch(tag.Address);
            tag.Properties["TagTable"] = table;
            foreach (var flag in new[] { "ExternalAccessible", "ExternalVisible", "ExternalWritable" })
            {
                var value = item.Attr(flag);
                if (value != null)
                    tag.Properties[flag] = value;
            }
            _tags.Add(tag);
        }
    }

    // ---- interface members -------------------------------------------------------

    private sealed record FlatMember(string Path, XElement Element, string Datatype, string? StartValue, string? Description);

    /// <summary>
    /// Yields members in declaration order with dotted paths. Anonymous <c>Struct</c> members
    /// are expanded; a UDT or FB-typed member's embedded <c>Sections</c> (its type's members,
    /// spec §2) are not, since the type itself describes them.
    /// </summary>
    private static IEnumerable<FlatMember> Flatten(IEnumerable<XElement> members, string? prefix)
    {
        foreach (var member in members)
        {
            var name = (string?)member.Attribute("Name") ?? "?";
            var path = prefix == null ? name : $"{prefix}.{name}";
            var datatype = (string?)member.Attribute("Datatype") ?? string.Empty;
            yield return new FlatMember(path, member, datatype,
                member.Child("StartValue")?.Value,
                SimaticMlXml.MultiLanguageComment(member.Child("Comment")));

            var nested = member.Children("Member").ToList();
            if (nested.Count > 0)
            {
                foreach (var child in Flatten(nested, path))
                    yield return child;
            }
        }
    }

    private static TagInfo ToTag(FlatMember member, string scope, string usage)
    {
        var (dataType, _, dims) = SplitDatatype(member.Datatype);
        var tag = new TagInfo
        {
            Name = member.Path,
            DataType = dataType,
            Scope = scope,
            Usage = usage,
            Description = member.Description,
            InitialValue = member.StartValue,
            ArrayDimensions = dims
        };
        if (member.Element.Attribute("Remanence") is { } remanence)
            tag.Properties["Remanence"] = remanence.Value;
        return tag;
    }

    /// <summary>
    /// Splits <c>Array[0..31] of Int</c> into element type <c>Int</c>, bounds <c>0..31</c> and
    /// lengths <c>[32]</c>. Lengths are null when a bound is symbolic or variable (<c>*</c>).
    /// User type names lose their quotes.
    /// </summary>
    internal static (string DataType, string? Dimensions, int[]? Lengths) SplitDatatype(string datatype)
    {
        var match = ArrayType.Match(datatype.Trim());
        if (!match.Success)
            return (SimaticMlXml.Unquote(datatype), null, null);

        var dims = match.Groups["dims"].Value.Trim();
        List<int>? lengths = new();
        foreach (var range in dims.Split(','))
        {
            var bounds = range.Split("..", StringSplitOptions.TrimEntries);
            if (bounds.Length == 2 &&
                long.TryParse(bounds[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var low) &&
                long.TryParse(bounds[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var high) &&
                high >= low && high - low < int.MaxValue)
            {
                lengths?.Add((int)(high - low + 1));
            }
            else
            {
                lengths = null;
                break;
            }
        }

        return (SimaticMlXml.Unquote(match.Groups["type"].Value), dims, lengths?.ToArray());
    }
}
