// Parts of this parser (ACD component tree navigation, task/program/module attribute
// offsets, data type class flags and the identity-to-catalog-number table) are ported
// from hutcheb/acd (https://github.com/hutcheb/acd, acd/l5x/elements.py and
// acd/l5x/catalog_numbers.py), Copyright the hutcheb/acd contributors, licensed under
// the Apache License, Version 2.0. See PlcInfo.Tests/Fixtures/THIRD_PARTY_NOTICES.md.

using System.Buffers.Binary;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using PlcInfo.Models;

namespace PlcInfo.Parsing;

/// <summary>
/// Parser for Rockwell Studio 5000 / RSLogix 5000 project files (<c>.ACD</c>).
/// </summary>
/// <remarks>
/// The ACD format is proprietary and undocumented. This parser reads the subset that the
/// reverse-engineering work in hutcheb/acd made reliable, and marks the result as partial
/// (<c>Metadata["ParseStatus"] = "Partial"</c>):
/// <list type="bullet">
/// <item><c>QuickInfo.XML</c>: controller name, description, device identity and software version.</item>
/// <item><c>TagInfo.XML</c>: controller and program tags (name, data type, dimensions,
/// description, external access) and data type members.</item>
/// <item><c>Comps.Dat</c>: programs, routines and their languages, tasks, Add-On Instructions,
/// modules and which data types are user-defined.</item>
/// <item><c>SbRegion.Dat</c>: ladder (RLL) rung text.</item>
/// </list>
/// Structured Text, FBD and SFC bodies, comments, tag values, alias targets and module
/// configuration are not decoded. For complete content, export the project to L5X.
/// </remarks>
public class RockwellAcdParser : IPlcProjectParser
{
    /// <summary>
    /// The user-facing notice stored as the first line of <c>Metadata["Warnings"]</c>.
    /// </summary>
    public const string PartialSupportWarning =
        "ACD support is partial (no ST/FBD/SFC logic, rung comments or tag values); export to L5X from Studio 5000 for full content.";

    private const uint ProgramMainRoutineAttribute = 0x12D;
    private const uint ProgramFaultRoutineAttribute = 0x66;

    // (vendor, product type, product code) -> catalog number. The ACD stores only the CIP
    // identity; this table comes from hutcheb/acd acd/l5x/catalog_numbers.py.
    private static readonly Dictionary<(int Vendor, int ProductType, int ProductCode), string> CatalogNumbers = new()
    {
        [(1, 0, 18)] = "ETHERNET-MODULE",
        [(1, 0, 28)] = "RHINOBP-DRIVE-PERIPHERAL-MODULE",
        [(1, 7, 11)] = "1756-IB16",
        [(1, 7, 30)] = "1756-OW16I",
        [(1, 7, 34)] = "1794-IB16/A",
        [(1, 7, 35)] = "1794-OB16/A",
        [(1, 7, 37)] = "1794-OW8/A",
        [(1, 7, 156)] = "1794-IB32/A",
        [(1, 7, 397)] = "5094-IB16/A",
        [(1, 7, 399)] = "5094-OB16/A",
        [(1, 10, 7)] = "1756-IF8/A",
        [(1, 10, 25)] = "1794-IE8/B",
        [(1, 10, 26)] = "1794-OE4/B",
        [(1, 10, 153)] = "1794-IF8IH/A",
        [(1, 10, 154)] = "1794-OF8IH/A",
        [(1, 12, 90)] = "1794-AENT",
        [(1, 12, 166)] = "1756-EN2T",
        [(1, 12, 169)] = "1756-EN2F",
        [(1, 12, 258)] = "1756-EN4TR",
        [(1, 12, 261)] = "1794-AENTR",
        [(1, 12, 322)] = "5094-AEN2TR/A",
        [(1, 14, 72)] = "1769-L33ERM",
        [(1, 14, 93)] = "1756-L72",
        [(1, 14, 94)] = "1756-L73",
        [(1, 14, 164)] = "1756-L81E",
        [(1, 14, 165)] = "1756-L82E",
        [(1, 14, 166)] = "1756-L83E",
        [(1, 14, 167)] = "1756-L84E",
        [(1, 14, 168)] = "1756-L85E",
        [(1, 109, 6)] = "1794-IP4/B",
        [(1, 115, 323)] = "5094-IF8IH/A",
        [(1, 115, 324)] = "5094-OF8IH/A",
        [(1, 123, 1168)] = "PowerFlex 753-NET-E",
        [(1, 142, 1168)] = "PowerFlex 753-ENETR",
        [(1, 143, 2192)] = "PowerFlex 755-EENET",
        [(1182, 0, 4162)] = "Promag_53/A",
        [(1182, 0, 4177)] = "Promass_83/A",
        [(1182, 43, 4154)] = "Promag_100",
        [(1182, 43, 4155)] = "Promass_300_500",
        [(1182, 43, 4156)] = "Promass_300_500",
        [(1182, 43, 4170)] = "Promass_100",
    };

    private static readonly Regex SavedVersion = new(@"Saved - (V(\d+)\.(\d+)\.\d+/\S+)", RegexOptions.CultureInvariant);
    private static readonly Regex SoftwareVersion = new(@"v(\d+\.\d+)\s*$", RegexOptions.CultureInvariant);

    /// <inheritdoc/>
    public bool CanParse(string filePath)
    {
        return Path.GetExtension(filePath).Equals(".ACD", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public Task<PlcProject> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        // Decoding is synchronous CPU and file work; keep it off the caller's thread.
        return Task.Run(() => Parse(filePath, cancellationToken), cancellationToken);
    }

    private static PlcProject Parse(string filePath, CancellationToken cancellationToken)
    {
        var container = RockwellAcdContainer.Open(filePath);
        var warnings = new List<string> { PartialSupportWarning };
        var metadata = new Dictionary<string, string>
        {
            ["ParseStatus"] = "Partial",
        };

        var quickInfo = LoadXml(container, "QuickInfo.XML", warnings);
        var tagInfo = LoadXml(container, "TagInfo.XML", warnings);
        cancellationToken.ThrowIfCancellationRequested();

        RockwellAcdDatabase? database = null;
        AcdComponent? controllerNode = null;
        try
        {
            var comps = container.ReadMember("Comps.Dat");
            if (comps is null)
            {
                warnings.Add("The ACD has no Comps.Dat, so programs, routines, tasks and modules are missing.");
            }
            else
            {
                database = RockwellAcdDatabase.Load(comps, container.ReadMember("SbRegion.Dat"));
                var roots = database.Children(0).Where(c => c.RecordType == 256).ToList();
                if (roots.Count == 1)
                    controllerNode = roots[0];
                else
                    warnings.Add("The ACD component database has no single controller node, so programs, routines, tasks and modules are missing.");
            }
        }
        catch (InvalidDataException ex)
        {
            database = null;
            warnings.Add($"The ACD component database could not be decoded ({ex.Message}), so programs, routines, tasks and modules are missing.");
        }
        cancellationToken.ThrowIfCancellationRequested();

        var controllerName = quickInfo?.Root?.Attribute("Name")?.Value
            ?? tagInfo?.Root?.Attribute("Name")?.Value
            ?? controllerNode?.Name;
        if (controllerName is null)
            throw new InvalidOperationException(
                $"'{Path.GetFileName(filePath)}' is an ACD file, but its controller information could not be read. " +
                "Open it in Studio 5000 and export it to L5X, then parse the L5X.");

        ReadVersion(container, quickInfo, metadata);

        var controller = ParseController(controllerName, quickInfo);
        var programTags = ParseProgramTags(tagInfo);
        var programs = new List<ProgramInfo>();
        var tasks = new List<TaskInfo>();
        var modules = new List<IoModuleInfo>();
        var dataTypes = new List<DataTypeInfo>();

        if (database is not null && controllerNode is not null)
        {
            programs.AddRange(ParsePrograms(database, controllerNode, programTags, warnings));
            programs.AddRange(ParseAddOnInstructions(database, controllerNode, tagInfo));
            tasks.AddRange(ParseTasks(database, controllerNode, warnings));
            modules.AddRange(ParseModules(database, controllerNode));
            dataTypes.AddRange(ParseUserDataTypes(database, controllerNode, tagInfo, warnings));
            warnings.AddRange(database.Warnings);
        }
        else
        {
            // Without the component tree, fall back to the program names known from TagInfo.XML.
            programs.AddRange(programTags.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => new ProgramInfo
            {
                Name = p.Key,
                Type = "Normal",
                LocalTags = p.Value,
            }));
        }

        metadata["Warnings"] = string.Join("\n", warnings);

        return new PlcProject
        {
            Vendor = "Rockwell Automation",
            Format = "ACD",
            FilePath = filePath,
            Controller = controller,
            Tasks = tasks,
            Programs = programs,
            Tags = ParseTags(tagInfo?.Root?.Element("Tags"), "Controller"),
            IoModules = modules,
            DataTypes = dataTypes,
            LastModified = File.GetLastWriteTime(filePath),
            Metadata = metadata,
        };
    }

    /// <summary>
    /// QuickInfo.XML and TagInfo.XML are gzip-compressed UTF-16 XML documents.
    /// </summary>
    private static XDocument? LoadXml(RockwellAcdContainer container, string name, List<string> warnings)
    {
        var bytes = container.ReadMember(name);
        if (bytes is null)
        {
            warnings.Add($"The ACD has no {name}, so information from it is missing.");
            return null;
        }

        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(bytes), settings);
            return XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            warnings.Add($"{name} in the ACD is not valid XML ({ex.Message}), so information from it is missing.");
            return null;
        }
    }

    private static void ReadVersion(RockwellAcdContainer container, XDocument? quickInfo, Dictionary<string, string> metadata)
    {
        // QuickInfo: <SWVersion String="RSLogix 5000 v34.01"/>
        var swVersion = quickInfo?.Root?.Element("SWVersion")?.Attribute("String")?.Value;
        if (swVersion is not null)
        {
            metadata["SoftwareVersion"] = swVersion;
            var match = SoftwareVersion.Match(swVersion);
            if (match.Success)
                metadata["SoftwareRevision"] = match.Groups[1].Value;
        }

        // Version.Log: one "<timestamp>: Saved - V34.01.00/3843.001" line per save.
        var log = container.ReadMember("Version.Log");
        if (log is null)
            return;
        var lastSave = System.Text.Encoding.ASCII.GetString(log)
            .Split('\n')
            .LastOrDefault(l => SavedVersion.IsMatch(l));
        if (lastSave is null)
            return;
        var saved = SavedVersion.Match(lastSave);
        metadata["LastSavedBuild"] = saved.Groups[1].Value;
        int colon = lastSave.IndexOf(": Saved", StringComparison.Ordinal);
        if (colon > 0)
            metadata["LastSaved"] = lastSave[..colon].Trim();
        metadata.TryAdd("SoftwareRevision", $"{int.Parse(saved.Groups[2].Value, CultureInfo.InvariantCulture)}.{saved.Groups[3].Value}");
    }

    private static ControllerInfo ParseController(string name, XDocument? quickInfo)
    {
        var root = quickInfo?.Root;
        var identity = root?.Element("DeviceIdentity");
        var properties = new Dictionary<string, string>();

        string type = "";
        string firmware = "";
        if (identity is not null)
        {
            int vendor = IntAttribute(identity, "VendorID");
            int productType = IntAttribute(identity, "ProductType");
            int productCode = IntAttribute(identity, "ProductCode");
            type = CatalogNumbers.GetValueOrDefault((vendor, productType, productCode), "");
            var major = identity.Attribute("MajorRevision")?.Value;
            var minor = identity.Attribute("MinorRevision")?.Value;
            if (!string.IsNullOrEmpty(major))
                firmware = string.IsNullOrEmpty(minor) ? major : $"{major}.{minor}";
            properties["VendorID"] = identity.Attribute("VendorID")?.Value ?? "";
            properties["ProductType"] = identity.Attribute("ProductType")?.Value ?? "";
            properties["ProductCode"] = identity.Attribute("ProductCode")?.Value ?? "";
        }

        if (TrimmedOrNull(root?.Element("Description")?.Value) is { } description)
            properties["Description"] = description;

        var slot = root?.Element("ControllerSlot")?.Attribute("SlotNumber")?.Value;
        return new ControllerInfo
        {
            Name = name,
            Type = type,
            Vendor = "Rockwell Automation",
            FirmwareRevision = firmware,
            Slot = int.TryParse(slot, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : null,
            Properties = properties,
        };
    }

    private static int IntAttribute(XElement element, string name) =>
        int.TryParse(element.Attribute(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : -1;

    private static Dictionary<string, IReadOnlyList<TagInfo>> ParseProgramTags(XDocument? tagInfo)
    {
        var result = new Dictionary<string, IReadOnlyList<TagInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var program in tagInfo?.Root?.Element("Programs")?.Elements("Program") ?? Enumerable.Empty<XElement>())
        {
            var name = program.Attribute("Name")?.Value;
            if (name is not null)
                result[name] = ParseTags(program.Element("Tags"), name);
        }
        return result;
    }

    /// <summary>
    /// TagInfo.XML lists tags as
    /// <c>&lt;Tag Name= DataType= ExtAcc= IOI=&gt;&lt;Description&gt;…&lt;/Description&gt;&lt;Dimensions&gt;&lt;Dim Size=/&gt;…</c>.
    /// It omits tags with External Access "None" and lists alias tags with their resolved
    /// data type (checked against the paired L5X export of ACDTestsWithAOI). Tags are
    /// sorted by name, as Studio 5000 exports them.
    /// </summary>
    private static IReadOnlyList<TagInfo> ParseTags(XElement? tagsElement, string scope)
    {
        if (tagsElement is null)
            return Array.Empty<TagInfo>();

        return tagsElement.Elements("Tag")
            .Select(t =>
            {
                var tag = new TagInfo
                {
                    Name = t.Attribute("Name")?.Value ?? "Unknown",
                    DataType = t.Attribute("DataType")?.Value ?? "",
                    Scope = scope,
                    Description = TrimmedOrNull(t.Element("Description")?.Value),
                    ArrayDimensions = ParseDims(t.Element("Dimensions")),
                };
                if (t.Attribute("ExtAcc")?.Value is { } access)
                    tag.Properties["ExternalAccess"] = access;
                return tag;
            })
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int[]? ParseDims(XElement? dimensions)
    {
        if (dimensions is null)
            return null;
        var sizes = dimensions.Elements("Dim")
            .Select(d => int.TryParse(d.Attribute("Size")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : (int?)null)
            .Where(n => n.HasValue)
            .Select(n => n!.Value)
            .ToArray();
        return sizes.Length > 0 ? sizes : null;
    }

    private static string? TrimmedOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static IEnumerable<ProgramInfo> ParsePrograms(
        RockwellAcdDatabase database,
        AcdComponent controller,
        Dictionary<string, IReadOnlyList<TagInfo>> programTags,
        List<string> warnings)
    {
        var collection = database.Child(controller.ObjectId, "RxProgramCollection");
        if (collection is null)
        {
            warnings.Add("The ACD has no program collection, so programs are missing.");
            yield break;
        }

        foreach (var program in database.Children(collection.ObjectId)
                     .Where(c => c.RecordType == 256)
                     .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            var rx = AcdRxRecord.TryParse(program.Body);
            var properties = new Dictionary<string, string>();
            if (rx?.AttributeUInt32(ProgramMainRoutineAttribute) is { } mainId && database.NameOf(mainId) is { } mainName)
                properties["MainRoutineName"] = mainName;
            if (rx?.AttributeUInt32(ProgramFaultRoutineAttribute) is { } faultId && faultId != 0 && database.NameOf(faultId) is { } faultName)
                properties["FaultRoutineName"] = faultName;

            yield return new ProgramInfo
            {
                Name = program.Name,
                Type = "Normal",
                Routines = ParseRoutines(database, program),
                LocalTags = programTags.GetValueOrDefault(program.Name, Array.Empty<TagInfo>()),
                Properties = properties,
            };
        }
    }

    private static IEnumerable<ProgramInfo> ParseAddOnInstructions(RockwellAcdDatabase database, AcdComponent controller, XDocument? tagInfo)
    {
        // Add-On Instructions exist from v16; older projects have no collection.
        var collection = database.Child(controller.ObjectId, "RxUDIDefinitionCollection");
        if (collection is null)
            yield break;

        var typeDescriptions = (tagInfo?.Root?.Element("DataTypes")?.Elements("DataType") ?? Enumerable.Empty<XElement>())
            .GroupBy(d => d.Attribute("Name")?.Value ?? "")
            .ToDictionary(g => g.Key, g => TrimmedOrNull(g.First().Element("Description")?.Value));

        foreach (var aoi in database.Children(collection.ObjectId)
                     .Where(c => c.RecordType == 256)
                     .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            yield return new ProgramInfo
            {
                Name = aoi.Name,
                Type = "AddOnInstruction",
                Language = "AOI",
                Description = typeDescriptions.GetValueOrDefault(aoi.Name),
                Routines = ParseRoutines(database, aoi),
            };
        }
    }

    private static IReadOnlyList<RoutineInfo> ParseRoutines(RockwellAcdDatabase database, AcdComponent owner)
    {
        var collection = database.Child(owner.ObjectId, "RxRoutineCollection");
        if (collection is null)
            return Array.Empty<RoutineInfo>();

        return database.Children(collection.ObjectId)
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(routine =>
            {
                // Routine type: u16 at body offset 0x3E (main record + 0x30), hutcheb/acd RoutineBuilder.
                var language = routine.Body.Length >= 0x40
                    ? BinaryPrimitives.ReadUInt16LittleEndian(routine.Body.AsSpan(0x3E)) switch
                    {
                        1 => "RLL",
                        2 => "FBD",
                        3 => "SFC",
                        4 => "ST",
                        5 => "External",
                        6 => "Encrypted",
                        _ => "TypeLess",
                    }
                    : "Unknown";

                var rungs = language == "RLL"
                    ? database.RungsOf(routine.ObjectId)
                        .Select((text, index) => new RungInfo
                        {
                            Number = index,
                            Text = text,
                            Instructions = RockwellL5XParser.TokenizeRung(text),
                        })
                        .ToList()
                    : new List<RungInfo>();

                return new RoutineInfo
                {
                    Name = routine.Name,
                    Type = language,
                    Language = language,
                    ProgramName = owner.Name,
                    RungCount = rungs.Count,
                    Rungs = rungs,
                };
            })
            .ToList();
    }

    /// <summary>
    /// Task settings live at fixed offsets (hutcheb/acd TaskBuilder): in records longer than
    /// 0x112E bytes they are at absolute body offsets, otherwise inside attribute 0x01.
    /// Scheduled programs are a u16 count followed by program comment ids.
    /// </summary>
    private static IEnumerable<TaskInfo> ParseTasks(RockwellAcdDatabase database, AcdComponent controller, List<string> warnings)
    {
        var collection = database.Child(controller.ObjectId, "RxTaskCollection");
        if (collection is null)
            yield break;

        var programCollection = database.Child(controller.ObjectId, "RxProgramCollection");
        var programsByScheduleId = new Dictionary<int, string>();
        foreach (var program in programCollection is null ? Array.Empty<AcdComponent>() : database.Children(programCollection.ObjectId))
        {
            if (AcdRxRecord.TryParse(program.Body) is { } rx)
                programsByScheduleId.TryAdd(rx.CommentId, program.Name);
        }

        foreach (var task in database.Children(collection.ObjectId).Where(c => c.RecordType == 256))
        {
            byte[] data;
            int rateOffset, typeOffset, priorityOffset, watchdogOffset, programOffset;
            if (task.Body.Length > 0x112E)
            {
                data = task.Body;
                (rateOffset, typeOffset, priorityOffset, watchdogOffset, programOffset) = (0x106C, 0x10F6, 0x10F8, 0x110A, 0x5A);
            }
            else
            {
                data = AcdRxRecord.TryParse(task.Body)?.Attributes.GetValueOrDefault(1u) ?? Array.Empty<byte>();
                (rateOffset, typeOffset, priorityOffset, watchdogOffset, programOffset) = (514, 652, 654, 802, 0);
            }

            string? type = data.Length >= watchdogOffset + 4
                ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(typeOffset)) switch
                {
                    1 => "EVENT",
                    2 => "PERIODIC",
                    4 => "CONTINUOUS",
                    _ => null,
                }
                : null;

            if (type is null)
            {
                warnings.Add($"Task '{task.Name}': its settings could not be decoded.");
                yield return new TaskInfo { Name = task.Name };
                continue;
            }

            int programCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(programOffset));
            var programNames = new List<string>();
            if (programCount <= (rateOffset - programOffset - 2) / 4)
            {
                for (int i = 0; i < programCount; i++)
                {
                    int id = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(programOffset + 2 + i * 4));
                    if (programsByScheduleId.TryGetValue(id, out var name))
                        programNames.Add(name);
                }
            }
            else
            {
                warnings.Add($"Task '{task.Name}': its scheduled programs could not be decoded.");
            }

            uint rateMicroseconds = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(rateOffset));
            uint watchdogMicroseconds = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(watchdogOffset));
            yield return new TaskInfo
            {
                Name = task.Name,
                Type = type,
                Priority = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(priorityOffset)),
                // L5X expresses Rate and Watchdog in milliseconds; the ACD stores microseconds.
                Rate = type == "CONTINUOUS" ? null : rateMicroseconds / 1000.0,
                WatchdogTimeout = watchdogMicroseconds / 1000.0,
                ProgramNames = programNames,
            };
        }
    }

    /// <summary>
    /// Module identity lives in attribute 0x01 of each RxMapDeviceCollection child
    /// (hutcheb/acd ModuleBuilder): vendor u16 @2, product type u16 @4, product code u16 @6,
    /// major revision (low 7 bits) @8, minor revision @9.
    /// </summary>
    private static IEnumerable<IoModuleInfo> ParseModules(RockwellAcdDatabase database, AcdComponent controller)
    {
        var collection = database.Child(controller.ObjectId, "RxMapDeviceCollection");
        if (collection is null)
            yield break;

        foreach (var module in database.Children(collection.ObjectId))
        {
            // Unnamed peripheral modules are stored as "$hex$"; L5X exports omit their name.
            var name = module.Name.StartsWith('$') && module.Name.EndsWith('$') ? "Unknown" : module.Name;
            var info = new IoModuleInfo { Name = name, Type = "Unknown" };

            var identity = AcdRxRecord.TryParse(module.Body) is { CipType: 0x69 } rx
                ? rx.Attributes.GetValueOrDefault(1u)
                : null;
            if (identity is { Length: >= 0x30 })
            {
                int vendor = BinaryPrimitives.ReadUInt16LittleEndian(identity.AsSpan(2));
                int productType = BinaryPrimitives.ReadUInt16LittleEndian(identity.AsSpan(4));
                int productCode = BinaryPrimitives.ReadUInt16LittleEndian(identity.AsSpan(6));
                var catalog = CatalogNumbers.GetValueOrDefault((vendor, productType, productCode));
                info.CatalogNumber = catalog;
                info.Type = RockwellL5XParser.DetermineModuleType(catalog ?? "");
                info.Vendor = vendor.ToString(CultureInfo.InvariantCulture);
                info.Properties["Major"] = (identity[8] & 0x7F).ToString(CultureInfo.InvariantCulture);
                info.Properties["Minor"] = identity[9].ToString(CultureInfo.InvariantCulture);
                info.Properties["ProductCode"] = productCode.ToString(CultureInfo.InvariantCulture);
                info.Properties["ProductType"] = productType.ToString(CultureInfo.InvariantCulture);
            }
            yield return info;
        }
    }

    /// <summary>
    /// Data types whose class flags (attribute 0x67 "built in", 0x69 "module defined") are
    /// clear are user-defined (hutcheb/acd DataTypeBuilder). Their members and descriptions
    /// come from TagInfo.XML; hidden members (the SINT hosts of BOOL bits) are skipped,
    /// matching how the L5X parser reports UDTs.
    /// </summary>
    private static IEnumerable<DataTypeInfo> ParseUserDataTypes(
        RockwellAcdDatabase database,
        AcdComponent controller,
        XDocument? tagInfo,
        List<string> warnings)
    {
        var collection = database.Child(controller.ObjectId, "RxDataTypeCollection");
        if (collection is null)
            yield break;

        var definitions = (tagInfo?.Root?.Element("DataTypes")?.Elements("DataType") ?? Enumerable.Empty<XElement>())
            .GroupBy(d => d.Attribute("Name")?.Value ?? "")
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var type in database.Children(collection.ObjectId).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            var rx = AcdRxRecord.TryParse(type.Body);
            if (rx?.AttributeUInt32(0x67) is not { } builtIn || rx.AttributeUInt32(0x69) is not { } moduleDefined)
                continue;
            if ((builtIn & 0x03) != 0 || moduleDefined != 0)
                continue;

            if (!definitions.TryGetValue(type.Name, out var definition))
            {
                warnings.Add($"Data type '{type.Name}': its members are not listed in the ACD.");
                yield return new DataTypeInfo { Name = type.Name, Properties = { ["Class"] = "User" } };
                continue;
            }

            var members = (definition.Element("Members")?.Elements("Member") ?? Enumerable.Empty<XElement>())
                .Where(m => m.Attribute("Hidden")?.Value != "true")
                .Select(m =>
                {
                    var member = new DataTypeMember
                    {
                        Name = m.Attribute("Name")?.Value ?? "",
                        DataType = m.Attribute("DataType")?.Value ?? "",
                        Dimensions = ParseDims(m.Element("Dimensions")) is { } dims
                            ? string.Join(" ", dims.Select(d => d.ToString(CultureInfo.InvariantCulture)))
                            : null,
                        Description = TrimmedOrNull(m.Element("Description")?.Value),
                    };
                    if (m.Attribute("ExtAcc")?.Value is { } access)
                        member.Properties["ExternalAccess"] = access;
                    return member;
                })
                .ToList();

            yield return new DataTypeInfo
            {
                Name = type.Name,
                Kind = "Struct",
                Description = TrimmedOrNull(definition.Element("Description")?.Value),
                Members = members,
                Properties = { ["Class"] = "User" },
            };
        }
    }
}
