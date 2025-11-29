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

        var xml = await File.ReadAllTextAsync(filePath, cancellationToken);

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new InvalidOperationException("Invalid L5X file: no root element", ex);
        }

        var root = doc.Root;
        if (root == null)
            throw new InvalidOperationException("Invalid L5X file: no root element");

        var controller = ParseController(root);
        var tasks = ParseTasks(root);
        var programs = ParsePrograms(root);
        var tags = ParseTags(root);
        var modules = ParseModules(root);

        // Calculate total rungs from all routines
        var totalRungs = programs.SelectMany(p => p.Routines).Sum(r => r.RungCount);

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
            FirmwareRevision = controllerElement.Attribute("MajorRev")?.Value + "." +
                              controllerElement.Attribute("MinorRev")?.Value,
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
            foreach (var taskElement in tasksElement.Elements("Task"))
            {
                var task = new TaskInfo
                {
                    Name = taskElement.Attribute("Name")?.Value ?? "Unknown",
                    Type = taskElement.Attribute("Type")?.Value ?? "Continuous",
                    Priority = int.TryParse(taskElement.Attribute("Priority")?.Value, out var p) ? p : 10,
                    Rate = double.TryParse(taskElement.Attribute("Rate")?.Value, out var r) ? r : null,
                    WatchdogTimeout = double.TryParse(taskElement.Attribute("Watchdog")?.Value, out var w) ? w : null,
                    IsEnabled = !bool.TryParse(taskElement.Attribute("DisableUpdateOutputs")?.Value, out var disabled) || !disabled,
                    ProgramNames = taskElement.Elements("ScheduledPrograms")
                        .SelectMany(sp => sp.Elements("ScheduledProgram"))
                        .Select(p => p.Attribute("Name")?.Value ?? "")
                        .Where(n => !string.IsNullOrEmpty(n))
                        .ToList(),
                    Properties = new Dictionary<string, string>
                    {
                        ["InhibitTask"] = taskElement.Attribute("InhibitTask")?.Value ?? "false"
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
            foreach (var programElement in programsElement.Elements("Program"))
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
            foreach (var aoiElement in aoisElement.Elements("AddOnInstructionDefinition"))
            {
                var routines = ParseRoutines(aoiElement);
                var parameters = ParseAoiParameters(aoiElement);

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
            foreach (var routineElement in routinesElement.Elements("Routine"))
            {
                var rungs = ParseRungs(routineElement);

                var routine = new RoutineInfo
                {
                    Name = routineElement.Attribute("Name")?.Value ?? "Unknown",
                    Type = routineElement.Attribute("Type")?.Value ?? "Main",
                    Language = DetermineLanguage(routineElement),
                    ProgramName = parentElement.Attribute("Name")?.Value,
                    Description = routineElement.Element("Description")?.Value,
                    RungCount = rungs.Count,
                    Rungs = rungs
                };
                routines.Add(routine);
            }
        }

        return routines;
    }

    private string DetermineLanguage(XElement routineElement)
    {
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
        var rllContent = routineElement.Element("RLLContent");

        if (rllContent != null)
        {
            int rungNumber = 0;
            foreach (var rungElement in rllContent.Elements("Rung"))
            {
                var instructions = ParseInstructions(rungElement);

                var rung = new RungInfo
                {
                    Number = rungNumber++,
                    Comment = rungElement.Element("Comment")?.Value,
                    Text = rungElement.Element("Text")?.Value,
                    Instructions = instructions,
                    Properties = new Dictionary<string, string>
                    {
                        ["Type"] = rungElement.Attribute("Type")?.Value ?? "N"
                    }
                };
                rungs.Add(rung);
            }
        }

        return rungs;
    }

    private IReadOnlyList<InstructionInfo> ParseInstructions(XElement rungElement)
    {
        var instructions = new List<InstructionInfo>();
        var text = rungElement.Element("Text")?.Value;

        if (!string.IsNullOrEmpty(text))
        {
            // Simple instruction parsing from text
            var parts = text.Split(new[] { '[', ']', '(', ')', ';' }, StringSplitOptions.RemoveEmptyEntries);
            int position = 0;

            foreach (var part in parts.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0 && char.IsUpper(trimmed[0]))
                {
                    var instruction = new InstructionInfo
                    {
                        Mnemonic = trimmed.Split(' ')[0],
                        Position = position++,
                        Type = DetermineInstructionType(trimmed.Split(' ')[0]),
                        Operands = trimmed.Contains(' ')
                            ? trimmed.Substring(trimmed.IndexOf(' ') + 1).Split(',').Select(o => o.Trim()).ToList()
                            : new List<string>()
                    };
                    instructions.Add(instruction);
                }
            }
        }

        return instructions;
    }

    private string DetermineInstructionType(string mnemonic)
    {
        return mnemonic switch
        {
            "XIC" or "XIO" or "OTL" or "OTU" => "Bit",
            "OTE" => "Output",
            "TON" or "TOF" or "RTO" => "Timer",
            "CTU" or "CTD" or "RES" => "Counter",
            "MOV" or "COP" or "FLL" => "Move",
            "ADD" or "SUB" or "MUL" or "DIV" => "Math",
            "EQU" or "NEQ" or "LES" or "GRT" => "Compare",
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
            foreach (var tagElement in tagsElement.Elements("Tag"))
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
            foreach (var tagElement in tagsElement.Elements("Tag"))
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
            foreach (var paramElement in paramsElement.Elements("Parameter"))
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

        // Parse array dimensions
        var dimensions = tagElement.Attribute("Dimensions")?.Value;
        if (!string.IsNullOrEmpty(dimensions))
        {
            tag.ArrayDimensions = dimensions.Split(' ').Select(int.Parse).ToArray();
        }

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

    private IReadOnlyList<IoModuleInfo> ParseModules(XElement root)
    {
        var modules = new List<IoModuleInfo>();
        var modulesElement = root.Element("Controller")?.Element("Modules");

        if (modulesElement != null)
        {
            foreach (var moduleElement in modulesElement.Elements("Module"))
            {
                var catalogNumber = moduleElement.Attribute("CatalogNumber")?.Value ?? "";
                var module = new IoModuleInfo
                {
                    Name = moduleElement.Attribute("Name")?.Value ?? "Unknown",
                    CatalogNumber = catalogNumber,
                    Type = DetermineModuleType(catalogNumber),
                    Vendor = moduleElement.Attribute("Vendor")?.Value ?? "Rockwell Automation",
                    Location = $"Slot {moduleElement.Attribute("Slot")?.Value ?? "?"}",
                    CommFormat = moduleElement.Element("Communications")?.Element("ConfigTag")?.Attribute("ConfigSize")?.Value,
                    ParentModule = moduleElement.Attribute("ParentModule")?.Value,
                    Properties = new Dictionary<string, string>
                    {
                        ["Major"] = moduleElement.Attribute("Major")?.Value ?? "",
                        ["Minor"] = moduleElement.Attribute("Minor")?.Value ?? "",
                        ["ProductCode"] = moduleElement.Attribute("ProductCode")?.Value ?? "",
                        ["ProductType"] = moduleElement.Attribute("ProductType")?.Value ?? "",
                        ["Inhibited"] = moduleElement.Attribute("Inhibited")?.Value ?? "false",
                        ["MajorFault"] = moduleElement.Attribute("MajorFault")?.Value ?? "false"
                    }
                };

                // Parse ports for channel count
                var ports = moduleElement.Element("Ports")?.Elements("Port").Count() ?? 0;
                module.ChannelCount = ports;

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