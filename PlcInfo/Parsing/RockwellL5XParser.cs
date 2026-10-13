using System.Globalization;
using System.Xml.Linq;
using PlcInfo.Models;

namespace PlcInfo.Parsing;

/// <summary>
/// Parser for Rockwell L5X XML files.
/// </summary>
public class RockwellL5XParser : IPlcProjectParser
{
    /// <inheritdoc/>
    public bool CanParse(string filePath)
    {
        return Path.GetExtension(filePath).Equals(".L5X", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public async Task<PlcProject> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        XDocument doc;
        try
        {
            // Load from a stream so the XML declaration's encoding is honoured
            // and the file isn't buffered twice (string + DOM).
            await using var stream = File.OpenRead(filePath);
            doc = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new InvalidOperationException($"Invalid L5X file: {ex.Message}", ex);
        }

        var root = doc.Root;
        if (root == null)
            throw new InvalidOperationException("Invalid L5X file: no root element");

        var controller = ParseController(root);
        var tasks = ParseTasks(root);
        var programs = ParsePrograms(root);
        var tags = ParseTags(root);
        var modules = ParseModules(root);
        var dataTypes = ParseDataTypes(root);

        return new PlcProject
        {
            Vendor = "Rockwell Automation",
            Format = "L5X",
            FilePath = filePath,
            Controller = controller,
            Tasks = tasks,
            Programs = programs,
            Tags = tags,
            IoModules = modules,
            DataTypes = dataTypes,
            LastModified = File.GetLastWriteTime(filePath),
            Metadata = new Dictionary<string, string>
            {
                ["SchemaRevision"] = root.Attribute("SchemaRevision")?.Value ?? "",
                ["SoftwareRevision"] = root.Attribute("SoftwareRevision")?.Value ?? "",
                ["TargetType"] = root.Attribute("TargetType")?.Value ?? "",
                ["ContainsContext"] = root.Attribute("ContainsContext")?.Value ?? "false"
            }
        };
    }

    /// <summary>
    /// Partial exports (rung, routine, program...) include referenced components with
    /// Use="Context" for import only. They are not part of the exported content.
    /// </summary>
    private static IEnumerable<XElement> Content(XElement? parent, string name) =>
        parent?.Elements(name).Where(e => e.Attribute("Use")?.Value != "Context") ?? Enumerable.Empty<XElement>();

    /// <summary>
    /// Returns the element without online-edit copies: content elements can repeat
    /// with OnlineEditType = Original | PendingEdits | TestEdits.
    /// </summary>
    private static XElement? OriginalContent(XElement parent, string name) =>
        parent.Elements(name).FirstOrDefault(e => e.Attribute("OnlineEditType") is null || e.Attribute("OnlineEditType")!.Value == "Original")
        ?? parent.Element(name);

    private ControllerInfo? ParseController(XElement root)
    {
        var controllerElement = root.Element("Controller");
        if (controllerElement == null)
            return null;

        return new ControllerInfo
        {
            Name = controllerElement.Attribute("Name")?.Value ?? "Unknown",
            Type = controllerElement.Attribute("ProcessorType")?.Value ?? "",
            Vendor = "Rockwell Automation",
            FirmwareRevision = FormatRevision(
                controllerElement.Attribute("MajorRev")?.Value,
                controllerElement.Attribute("MinorRev")?.Value),
            Properties = new Dictionary<string, string>
            {
                ["Use"] = controllerElement.Attribute("Use")?.Value ?? "",
                ["TimeSlice"] = controllerElement.Attribute("TimeSlice")?.Value ?? "",
                ["ShareUnusedTimeSlice"] = controllerElement.Attribute("ShareUnusedTimeSlice")?.Value ?? "",
                ["ProjectCreationDate"] = controllerElement.Attribute("ProjectCreationDate")?.Value ?? "",
                ["LastModifiedDate"] = controllerElement.Attribute("LastModifiedDate")?.Value ?? ""
            }
        };
    }

    private IReadOnlyList<TaskInfo> ParseTasks(XElement root)
    {
        var tasks = new List<TaskInfo>();
        var tasksElement = root.Element("Controller")?.Element("Tasks");

        if (tasksElement != null)
        {
            foreach (var taskElement in Content(tasksElement, "Task"))
            {
                var task = new TaskInfo
                {
                    Name = taskElement.Attribute("Name")?.Value ?? "Unknown",
                    Type = taskElement.Attribute("Type")?.Value ?? "Continuous",
                    Priority = int.TryParse(taskElement.Attribute("Priority")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 10,
                    Rate = double.TryParse(taskElement.Attribute("Rate")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : null,
                    WatchdogTimeout = double.TryParse(taskElement.Attribute("Watchdog")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : null,
                    // InhibitTask stops the task from running; DisableUpdateOutputs only
                    // suppresses output updates and does not mean the task is disabled.
                    IsEnabled = !bool.TryParse(taskElement.Attribute("InhibitTask")?.Value, out var inhibited) || !inhibited,
                    ProgramNames = taskElement.Elements("ScheduledPrograms")
                        .SelectMany(sp => sp.Elements("ScheduledProgram"))
                        .Select(p => p.Attribute("Name")?.Value ?? "")
                        .Where(n => !string.IsNullOrEmpty(n))
                        .ToList(),
                    Properties = new Dictionary<string, string>
                    {
                        ["InhibitTask"] = taskElement.Attribute("InhibitTask")?.Value ?? "false",
                        ["DisableUpdateOutputs"] = taskElement.Attribute("DisableUpdateOutputs")?.Value ?? "false"
                    }
                };
                tasks.Add(task);
            }
        }

        return tasks;
    }

    private IReadOnlyList<ProgramInfo> ParsePrograms(XElement root)
    {
        var programs = new List<ProgramInfo>();
        var programsElement = root.Element("Controller")?.Element("Programs");

        if (programsElement != null)
        {
            foreach (var programElement in Content(programsElement, "Program"))
            {
                var routines = ParseRoutines(programElement);
                var localTags = ParseProgramTags(programElement);

                var program = new ProgramInfo
                {
                    Name = programElement.Attribute("Name")?.Value ?? "Unknown",
                    Type = programElement.Attribute("Type")?.Value ?? "Normal",
                    Description = programElement.Element("Description")?.Value,
                    TaskName = programElement.Attribute("ScheduleInTask")?.Value,
                    Routines = routines,
                    LocalTags = localTags,
                    Properties = new Dictionary<string, string>
                    {
                        ["MainRoutineName"] = programElement.Attribute("MainRoutineName")?.Value ?? "",
                        ["FaultRoutineName"] = programElement.Attribute("FaultRoutineName")?.Value ?? "",
                        ["Disabled"] = programElement.Attribute("Disabled")?.Value ?? "false",
                        ["UseAsFolder"] = programElement.Attribute("UseAsFolder")?.Value ?? "false"
                    }
                };
                programs.Add(program);
            }
        }

        // Also check for Add-On Instructions (AOIs)
        var aoisElement = root.Element("Controller")?.Element("AddOnInstructionDefinitions");
        if (aoisElement != null)
        {
            foreach (var aoiElement in Content(aoisElement, "AddOnInstructionDefinition"))
            {
                var routines = ParseRoutines(aoiElement);
                // Parameters first (call signature), then the AOI's private local tags.
                var parameters = ParseAoiParameters(aoiElement).Concat(ParseAoiLocalTags(aoiElement)).ToList();

                var aoi = new ProgramInfo
                {
                    Name = aoiElement.Attribute("Name")?.Value ?? "Unknown",
                    Type = "AddOnInstruction",
                    Description = aoiElement.Element("Description")?.Value,
                    Language = "AOI",
                    Routines = routines,
                    LocalTags = parameters,
                    Properties = new Dictionary<string, string>
                    {
                        ["Revision"] = aoiElement.Attribute("Revision")?.Value ?? "",
                        ["RevisionExtension"] = aoiElement.Attribute("RevisionExtension")?.Value ?? "",
                        ["Vendor"] = aoiElement.Attribute("Vendor")?.Value ?? ""
                    }
                };
                programs.Add(aoi);
            }
        }

        return programs;
    }

    private IReadOnlyList<RoutineInfo> ParseRoutines(XElement parentElement)
    {
        var routines = new List<RoutineInfo>();
        var routinesElement = parentElement.Element("Routines");

        if (routinesElement != null)
        {
            foreach (var routineElement in Content(routinesElement, "Routine"))
            {
                var rungs = ParseRungs(routineElement);
                var language = DetermineLanguage(routineElement);

                var routine = new RoutineInfo
                {
                    Name = routineElement.Attribute("Name")?.Value ?? "Unknown",
                    Type = routineElement.Attribute("Type")?.Value ?? "Main",
                    Language = language,
                    ProgramName = parentElement.Attribute("Name")?.Value,
                    Description = routineElement.Element("Description")?.Value,
                    RungCount = rungs.Count,
                    Rungs = rungs,
                    Source = language == "ST" ? ParseStructuredText(routineElement) : null
                };
                routines.Add(routine);
            }
        }

        return routines;
    }

    private string DetermineLanguage(XElement routineElement)
    {
        // The Type attribute (RLL/ST/FBD/SFC) is authoritative and is present even
        // on empty routines (e.g. Prescan/Postscan) that have no content element.
        var type = routineElement.Attribute("Type")?.Value;
        if (type is "RLL" or "ST" or "FBD" or "SFC")
            return type;

        if (routineElement.Element("RLLContent") != null)
            return "RLL";
        if (routineElement.Element("STContent") != null)
            return "ST";
        if (routineElement.Element("FBDContent") != null)
            return "FBD";
        if (routineElement.Element("SFCContent") != null)
            return "SFC";
        return "Unknown";
    }

    private IReadOnlyList<RungInfo> ParseRungs(XElement routineElement)
    {
        var rungs = new List<RungInfo>();
        var rllContent = OriginalContent(routineElement, "RLLContent");

        if (rllContent != null)
        {
            int rungNumber = 0;
            foreach (var rungElement in rllContent.Elements("Rung"))
            {
                var instructions = ParseInstructions(rungElement);
                var editState = rungElement.Attribute("Type")?.Value ?? "N";
                var number = int.TryParse(rungElement.Attribute("Number")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                    ? n
                    : rungNumber;
                rungNumber = number + 1;

                var rung = new RungInfo
                {
                    Number = number,
                    Comment = rungElement.Element("Comment")?.Value.Trim(),
                    Text = rungElement.Element("Text")?.Value.Trim(),
                    Instructions = instructions,
                    // Rung@Type is the online-edit state (N = normal), not the language.
                    EditState = editState,
                    Properties = new Dictionary<string, string>
                    {
                        ["Type"] = editState
                    }
                };
                rungs.Add(rung);
            }
        }

        return rungs;
    }

    /// <summary>
    /// Joins STContent/Line elements (0-based Number, empty lines kept) into source text.
    /// </summary>
    private static string? ParseStructuredText(XElement routineElement)
    {
        var content = OriginalContent(routineElement, "STContent");
        if (content == null)
            return null;

        var lines = content.Elements("Line")
            .Select((line, index) => (
                Number: int.TryParse(line.Attribute("Number")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : index,
                // Exports pretty-print each CDATA line with surrounding line breaks.
                Text: line.Value.Trim('\r', '\n')))
            .OrderBy(l => l.Number)
            .Select(l => l.Text);
        return string.Join("\n", lines);
    }

    private IReadOnlyList<DataTypeInfo> ParseDataTypes(XElement root)
    {
        var dataTypes = new List<DataTypeInfo>();
        foreach (var typeElement in Content(root.Element("Controller")?.Element("DataTypes"), "DataType"))
        {
            var members = typeElement.Element("Members")?.Elements("Member")
                // BOOL members are stored as BIT members of hidden host SINTs; hide the hosts.
                .Where(m => m.Attribute("Hidden")?.Value != "true")
                .Select(m =>
                {
                    var dataType = m.Attribute("DataType")?.Value ?? "";
                    var dimension = m.Attribute("Dimension")?.Value;
                    var member = new DataTypeMember
                    {
                        Name = m.Attribute("Name")?.Value ?? "",
                        DataType = dataType == "BIT" ? "BOOL" : dataType,
                        Dimensions = string.IsNullOrEmpty(dimension) || dimension == "0" ? null : dimension,
                        Description = m.Element("Description")?.Value.Trim()
                    };
                    foreach (var attribute in new[] { "Radix", "ExternalAccess", "Target", "BitNumber" })
                    {
                        if (m.Attribute(attribute) is { } a)
                            member.Properties[attribute] = a.Value;
                    }
                    return member;
                })
                .ToList() ?? new List<DataTypeMember>();

            dataTypes.Add(new DataTypeInfo
            {
                Name = typeElement.Attribute("Name")?.Value ?? "Unknown",
                Kind = "Struct",
                Description = typeElement.Element("Description")?.Value.Trim(),
                Members = members,
                Properties = new Dictionary<string, string>
                {
                    ["Family"] = typeElement.Attribute("Family")?.Value ?? "",
                    ["Class"] = typeElement.Attribute("Class")?.Value ?? ""
                }
            });
        }
        return dataTypes;
    }

    private IReadOnlyList<TagInfo> ParseAoiLocalTags(XElement aoiElement)
    {
        return Content(aoiElement.Element("LocalTags"), "LocalTag")
            .Select(local => new TagInfo
            {
                Name = local.Attribute("Name")?.Value ?? "Unknown",
                DataType = local.Attribute("DataType")?.Value ?? "",
                Scope = "AOI",
                Usage = "Local",
                Description = local.Element("Description")?.Value,
                ArrayDimensions = ParseDimensions(local.Attribute("Dimensions")?.Value),
                Properties = new Dictionary<string, string>
                {
                    ["ExternalAccess"] = local.Attribute("ExternalAccess")?.Value ?? ""
                }
            })
            .ToList();
    }

    private IReadOnlyList<InstructionInfo> ParseInstructions(XElement rungElement)
    {
        var text = rungElement.Element("Text")?.Value;
        return string.IsNullOrWhiteSpace(text)
            ? Array.Empty<InstructionInfo>()
            : TokenizeRung(text);
    }

    /// <summary>
    /// Tokenizes Logix neutral rung text such as
    /// <c>[XIC(A),XIO(B)]CPT(Dest,(X+Y)*2)OTE(Out);</c> into instructions.
    /// Branch brackets and branch commas are structural; operands are split on
    /// commas that are not nested inside parentheses or array brackets.
    /// </summary>
    internal static IReadOnlyList<InstructionInfo> TokenizeRung(string text)
    {
        var instructions = new List<InstructionInfo>();
        int i = 0;

        while (i < text.Length)
        {
            var c = text[i];
            if (!(char.IsLetter(c) || c == '_'))
            {
                // Whitespace, branch delimiters '[', ']', ',' and the terminating ';'.
                i++;
                continue;
            }

            int start = i;
            while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                i++;
            var mnemonic = text[start..i];

            int j = i;
            while (j < text.Length && char.IsWhiteSpace(text[j]))
                j++;

            if (j >= text.Length || text[j] != '(')
            {
                // A bare identifier outside an instruction is not an instruction.
                continue;
            }

            i = j + 1;
            var operands = new List<string>();
            var operand = new System.Text.StringBuilder();
            int depth = 0;

            while (i < text.Length)
            {
                c = text[i++];
                if (c == '(' || c == '[')
                {
                    depth++;
                }
                else if (c == ')' || c == ']')
                {
                    if (depth == 0 && c == ')')
                        break;
                    if (depth > 0)
                        depth--;
                }
                else if (c == ',' && depth == 0)
                {
                    operands.Add(operand.ToString().Trim());
                    operand.Clear();
                    continue;
                }
                operand.Append(c);
            }

            var last = operand.ToString().Trim();
            if (last.Length > 0 || operands.Count > 0)
                operands.Add(last);

            instructions.Add(new InstructionInfo
            {
                Mnemonic = mnemonic,
                Position = instructions.Count,
                Type = DetermineInstructionType(mnemonic),
                Operands = operands
            });
        }

        return instructions;
    }

    private static string FormatRevision(string? major, string? minor)
    {
        if (string.IsNullOrEmpty(major))
            return string.Empty;
        return string.IsNullOrEmpty(minor) ? major : $"{major}.{minor}";
    }

    private static string DetermineInstructionType(string mnemonic)
    {
        return mnemonic switch
        {
            "XIC" or "XIO" or "OTL" or "OTU" or "ONS" or "OSR" or "OSF" => "Bit",
            "OTE" => "Output",
            "TON" or "TOF" or "RTO" => "Timer",
            "CTU" or "CTD" or "RES" => "Counter",
            "MOV" or "COP" or "CPS" or "FLL" => "Move",
            "ADD" or "SUB" or "MUL" or "DIV" or "CPT" => "Math",
            "EQU" or "NEQ" or "LES" or "GRT" or "LEQ" or "GEQ" or "LIM" => "Compare",
            "JSR" or "RET" or "JMP" or "LBL" => "Program Control",
            _ => "Function"
        };
    }

    private IReadOnlyList<TagInfo> ParseTags(XElement root)
    {
        var tags = new List<TagInfo>();
        var controllerElement = root.Element("Controller");

        // Controller-scoped tags
        var tagsElement = controllerElement?.Element("Tags");
        if (tagsElement != null)
        {
            foreach (var tagElement in Content(tagsElement, "Tag"))
            {
                var tag = ParseTag(tagElement, "Controller");
                tags.Add(tag);
            }
        }

        return tags;
    }

    private IReadOnlyList<TagInfo> ParseProgramTags(XElement programElement)
    {
        var tags = new List<TagInfo>();
        var tagsElement = programElement.Element("Tags");

        if (tagsElement != null)
        {
            foreach (var tagElement in Content(tagsElement, "Tag"))
            {
                var tag = ParseTag(tagElement, programElement.Attribute("Name")?.Value ?? "Program");
                tags.Add(tag);
            }
        }

        return tags;
    }

    private IReadOnlyList<TagInfo> ParseAoiParameters(XElement aoiElement)
    {
        var parameters = new List<TagInfo>();
        var paramsElement = aoiElement.Element("Parameters");

        if (paramsElement != null)
        {
            foreach (var paramElement in Content(paramsElement, "Parameter"))
            {
                var param = new TagInfo
                {
                    Name = paramElement.Attribute("Name")?.Value ?? "Unknown",
                    DataType = paramElement.Attribute("DataType")?.Value ?? "",
                    Scope = "AOI",
                    Usage = paramElement.Attribute("Usage")?.Value,
                    Description = paramElement.Element("Description")?.Value,
                    Properties = new Dictionary<string, string>
                    {
                        ["Required"] = paramElement.Attribute("Required")?.Value ?? "false",
                        ["Visible"] = paramElement.Attribute("Visible")?.Value ?? "true",
                        ["ExternalAccess"] = paramElement.Attribute("ExternalAccess")?.Value ?? "Read/Write"
                    }
                };
                parameters.Add(param);
            }
        }

        return parameters;
    }

    private TagInfo ParseTag(XElement tagElement, string scope)
    {
        var tagType = tagElement.Attribute("TagType")?.Value;
        var aliasFor = tagElement.Attribute("AliasFor")?.Value;

        var tag = new TagInfo
        {
            Name = tagElement.Attribute("Name")?.Value ?? "Unknown",
            DataType = tagElement.Attribute("DataType")?.Value ?? "",
            Scope = scope,
            Usage = tagElement.Attribute("Usage")?.Value ?? "",
            Description = tagElement.Element("Description")?.Value,
            AliasFor = aliasFor,
            IsIoTag = tagType == "IOTag",
            Properties = new Dictionary<string, string>
            {
                ["TagType"] = tagType ?? "",
                ["ExternalAccess"] = tagElement.Attribute("ExternalAccess")?.Value ?? "Read/Write",
                ["Constant"] = tagElement.Attribute("Constant")?.Value ?? "false",
                ["Hidden"] = tagElement.Attribute("Hidden")?.Value ?? "false",
                ["Radix"] = tagElement.Attribute("Radix")?.Value ?? "Decimal"
            }
        };

        tag.ArrayDimensions = ParseDimensions(tagElement.Attribute("Dimensions")?.Value);

        // Parse initial value if present
        var dataElement = tagElement.Element("Data");
        if (dataElement != null)
        {
            var formatElement = dataElement.Element("DataValue") ??
                               dataElement.Element("Structure") ??
                               dataElement.Element("Array");
            if (formatElement != null)
            {
                tag.InitialValue = formatElement.ToString();
            }
        }

        return tag;
    }

    /// <summary>
    /// Tag dimensions are space-separated in L5X ("2 3"); decorated data uses commas.
    /// Unparseable entries are skipped rather than failing the whole project.
    /// </summary>
    private static int[]? ParseDimensions(string? dimensions)
    {
        if (string.IsNullOrWhiteSpace(dimensions))
            return null;

        var parsed = dimensions
            .Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => int.TryParse(d, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : (int?)null)
            .Where(n => n.HasValue)
            .Select(n => n!.Value)
            .ToArray();
        return parsed.Length > 0 ? parsed : null;
    }

    private IReadOnlyList<IoModuleInfo> ParseModules(XElement root)
    {
        var modules = new List<IoModuleInfo>();
        var modulesElement = root.Element("Controller")?.Element("Modules");

        if (modulesElement != null)
        {
            foreach (var moduleElement in Content(modulesElement, "Module"))
            {
                var catalogNumber = moduleElement.Attribute("CatalogNumber")?.Value ?? "";
                var name = moduleElement.Attribute("Name")?.Value ?? "Unknown";
                var parent = moduleElement.Attribute("ParentModule")?.Value;
                var ports = moduleElement.Element("Ports")?.Elements("Port").ToList() ?? new List<XElement>();

                // Slot or IP address is the Address of the module's upstream port
                // (the port connecting it to its parent), not a module attribute.
                // The controller has no upstream port; its slot is its own backplane port.
                var upstream = ports.FirstOrDefault(p => p.Attribute("Upstream")?.Value == "true")
                    ?? (parent == name ? ports.FirstOrDefault(p => p.Attribute("Type")?.Value == "ICP") : null);
                var address = upstream?.Attribute("Address")?.Value;
                var portType = upstream?.Attribute("Type")?.Value;
                string? location = address == null
                    ? null
                    : portType is "Ethernet" ? address : $"Slot {address}";
                var module = new IoModuleInfo
                {
                    Name = name,
                    CatalogNumber = catalogNumber,
                    Type = DetermineModuleType(catalogNumber),
                    Vendor = moduleElement.Attribute("Vendor")?.Value ?? "Rockwell Automation",
                    Location = location,
                    CommFormat = moduleElement.Element("Communications")?.Attribute("CommMethod")?.Value,
                    // The controller ("Local") lists itself as its own parent.
                    ParentModule = parent == name ? null : parent,
                    Properties = new Dictionary<string, string>
                    {
                        ["Major"] = moduleElement.Attribute("Major")?.Value ?? "",
                        ["Minor"] = moduleElement.Attribute("Minor")?.Value ?? "",
                        ["ProductCode"] = moduleElement.Attribute("ProductCode")?.Value ?? "",
                        ["ProductType"] = moduleElement.Attribute("ProductType")?.Value ?? "",
                        ["Inhibited"] = moduleElement.Attribute("Inhibited")?.Value ?? "false",
                        ["MajorFault"] = moduleElement.Attribute("MajorFault")?.Value ?? "false",
                        ["ParentModPortId"] = moduleElement.Attribute("ParentModPortId")?.Value ?? "",
                        ["PortType"] = portType ?? ""
                    }
                };

                modules.Add(module);
            }
        }

        return modules;
    }

    private string DetermineModuleType(string catalogNumber)
    {
        if (string.IsNullOrEmpty(catalogNumber))
            return "Unknown";

        var upper = catalogNumber.ToUpper();

        if (upper.Contains("IB") || upper.Contains("IA") || upper.Contains("IV"))
            return "Digital Input";
        if (upper.Contains("OB") || upper.Contains("OA") || upper.Contains("OV") || upper.Contains("OW"))
            return "Digital Output";
        if (upper.Contains("IF") || upper.Contains("IE") || upper.Contains("IR") || upper.Contains("IT"))
            return "Analog Input";
        if (upper.Contains("OF") || upper.Contains("OE"))
            return "Analog Output";
        if (upper.Contains("EN"))
            return "Communication";
        if (upper.Contains("CNB") || upper.Contains("CN2"))
            return "ControlNet";
        if (upper.Contains("DNB") || upper.Contains("DHRIO"))
            return "DeviceNet";
        if (upper.Contains("MVI"))
            return "Interface";

        return "I/O Module";
    }
}