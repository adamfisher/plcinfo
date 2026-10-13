using System.Xml.Linq;
using PlcInfo.Models;

namespace PlcInfo.Parsing;

/// <summary>
/// Parser for CODESYS 3.5 project files (.project).
/// </summary>
public class CodesysParser : IPlcProjectParser
{
    /// <inheritdoc/>
    public bool CanParse(string filePath)
    {
        // CODESYS projects can have various extensions
        var extension = Path.GetExtension(filePath).ToLower();
        if (extension == ".project" || extension == ".export")
            return true;

        if (!File.Exists(filePath))
            return false;

        // Check if it's an XML file with CODESYS content
        if (extension == ".xml")
        {
            try
            {
                var firstLines = File.ReadLines(filePath).Take(5).ToList();
                return firstLines.Any(line =>
                    line.Contains("codesys", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("3S-Smart", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public async Task<PlcProject> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var doc = XDocument.Parse(content);

        var root = doc.Root;
        if (root == null)
            throw new InvalidOperationException("Invalid CODESYS project file: no root element");

        // Strip namespace for easier parsing
        StripNamespaces(root);

        var projectName = ExtractProjectName(root, filePath);
        var controller = ExtractControllerInfo(root, projectName);
        var tasks = ParseTasks(root);
        var programs = ParsePous(root);
        var tags = ParseGlobalVariables(root);
        var modules = ParseIoConfiguration(root);

        // Determine format based on file extension or content
        var extension = Path.GetExtension(filePath).ToLower();
        var format = extension == ".export" || root.Name.LocalName == "project"
            ? "CODESYS Export"
            : "CODESYS 3.5";

        return new PlcProject
        {
            Vendor = "CODESYS",
            Format = format,
            FilePath = filePath,
            Controller = controller,
            Tasks = tasks,
            Programs = programs,
            Tags = tags,
            IoModules = modules,
            LastModified = File.GetLastWriteTime(filePath),
            Metadata = new Dictionary<string, string>
            {
                ["CodesysVersion"] = root.Attribute("schemaVersion")?.Value ?? "3.5",
                ["ProjectId"] = root.Attribute("id")?.Value ?? "",
                ["CreatedBy"] = root.Element("fileHeader")?.Element("creationDateTime")?.Attribute("createdBy")?.Value ?? "",
                ["CompanyName"] = root.Element("fileHeader")?.Attribute("companyName")?.Value ?? "",
                ["ProductVersion"] = root.Element("fileHeader")?.Attribute("productVersion")?.Value ?? ""
            }
        };
    }

    private string ExtractProjectName(XElement root, string filePath)
    {
        return root.Element("fileHeader")?.Element("contentHeader")?.Attribute("name")?.Value ??
               root.Attribute("name")?.Value ??
               Path.GetFileNameWithoutExtension(filePath);
    }

    private ControllerInfo ExtractControllerInfo(XElement root, string projectName)
    {
        var deviceElement = root.Descendants("Device").FirstOrDefault() ??
                           root.Descendants("addData").FirstOrDefault(e => e.Element("data")?.Attribute("name")?.Value == "Device");

        var controller = new ControllerInfo
        {
            Name = projectName,
            Vendor = "CODESYS",
            Type = "CODESYS Runtime",
            Properties = new Dictionary<string, string>()
        };

        if (deviceElement != null)
        {
            controller.Type = deviceElement.Attribute("deviceType")?.Value ??
                             deviceElement.Element("DeviceIdentification")?.Element("Type")?.Value ??
                             "CODESYS Runtime";

            controller.FirmwareRevision = deviceElement.Element("DeviceIdentification")?.Element("Version")?.Value ?? "";

            // Extract device properties
            var props = deviceElement.Elements("Property");
            foreach (var prop in props)
            {
                var name = prop.Attribute("Name")?.Value;
                var value = prop.Value;
                if (!string.IsNullOrEmpty(name))
                {
                    controller.Properties[name] = value;
                }
            }
        }

        // Try to find target information
        var targetElement = root.Descendants("data").FirstOrDefault(e => e.Attribute("name")?.Value == "TargetSettings");
        if (targetElement != null)
        {
            controller.CpuType = targetElement.Element("value")?.Attribute("cpu")?.Value;
        }

        return controller;
    }

    private IReadOnlyList<TaskInfo> ParseTasks(XElement root)
    {
        var tasks = new List<TaskInfo>();

        // Find task configuration
        var taskElements = root.Descendants("Task").Concat(root.Descendants("task"));

        foreach (var taskElement in taskElements)
        {
            var task = new TaskInfo
            {
                Name = taskElement.Attribute("name")?.Value ?? "Task",
                Type = DetermineTaskType(taskElement),
                Priority = ParseTaskPriority(taskElement),
                Rate = ParseTaskInterval(taskElement),
                IsEnabled = !bool.TryParse(taskElement.Attribute("disabled")?.Value, out var disabled) || !disabled,
                Properties = new Dictionary<string, string>()
            };

            // Extract POUs called by this task
            var pouCalls = taskElement.Descendants("pouCall").Concat(taskElement.Descendants("call"));
            task.ProgramNames = pouCalls
                .Select(p => p.Attribute("name")?.Value ?? p.Value)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();

            // Extract additional properties
            var properties = taskElement.Elements("Property");
            foreach (var prop in properties)
            {
                var name = prop.Attribute("Name")?.Value;
                var value = prop.Value;
                if (!string.IsNullOrEmpty(name))
                {
                    task.Properties[name] = value;
                }
            }

            tasks.Add(task);
        }

        // If no tasks found, check for implicit task configuration in POUs
        if (tasks.Count == 0)
        {
            var implicitTask = new TaskInfo
            {
                Name = "MainTask",
                Type = "Cyclic",
                Priority = 1,
                Properties = new Dictionary<string, string> { ["Implicit"] = "true" }
            };

            // Find main program
            var mainPou = root.Descendants("pou").FirstOrDefault(p =>
                p.Attribute("name")?.Value?.Equals("MAIN", StringComparison.OrdinalIgnoreCase) == true ||
                p.Attribute("name")?.Value?.Equals("PLC_PRG", StringComparison.OrdinalIgnoreCase) == true);

            if (mainPou != null)
            {
                implicitTask.ProgramNames = new[] { mainPou.Attribute("name")?.Value ?? "MAIN" };
                tasks.Add(implicitTask);
            }
        }

        return tasks;
    }

    private string DetermineTaskType(XElement taskElement)
    {
        var type = taskElement.Attribute("type")?.Value?.ToLower();

        if (string.IsNullOrEmpty(type))
        {
            // Check for interval attribute to determine if it's periodic
            if (taskElement.Attribute("interval") != null)
                return "Periodic";
            if (taskElement.Attribute("event") != null)
                return "Event";
            return "Freewheeling";
        }

        return type switch
        {
            "cyclic" => "Cyclic",
            "periodic" => "Periodic",
            "freewheeling" => "Freewheeling",
            "event" => "Event",
            "external" => "External",
            "status" => "Status",
            _ => type
        };
    }

    private int ParseTaskPriority(XElement taskElement)
    {
        var priorityStr = taskElement.Attribute("priority")?.Value ??
                         taskElement.Element("Priority")?.Value;

        if (int.TryParse(priorityStr, out var priority))
            return priority;

        return 16; // Default CODESYS priority
    }

    private double? ParseTaskInterval(XElement taskElement)
    {
        var intervalStr = taskElement.Attribute("interval")?.Value ??
                         taskElement.Element("Interval")?.Value;

        if (string.IsNullOrEmpty(intervalStr))
            return null;

        // Parse interval in CODESYS format (e.g., "T#10ms", "10ms", "0.01s")
        intervalStr = intervalStr.Replace("T#", "").Replace("t#", "");

        if (intervalStr.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
        {
            var value = intervalStr.Replace("ms", "", StringComparison.OrdinalIgnoreCase);
            if (double.TryParse(value, out var ms))
                return ms;
        }
        else if (intervalStr.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            var value = intervalStr.Replace("s", "", StringComparison.OrdinalIgnoreCase);
            if (double.TryParse(value, out var s))
                return s * 1000;
        }
        else if (intervalStr.EndsWith("us", StringComparison.OrdinalIgnoreCase))
        {
            var value = intervalStr.Replace("us", "", StringComparison.OrdinalIgnoreCase);
            if (double.TryParse(value, out var us))
                return us / 1000;
        }

        // Try to parse as direct number (assumed milliseconds)
        if (double.TryParse(intervalStr, out var direct))
            return direct;

        return null;
    }

    private IReadOnlyList<ProgramInfo> ParsePous(XElement root)
    {
        var programs = new List<ProgramInfo>();

        // Find all POUs (Program Organization Units)
        var pouElements = root.Descendants("pou").Concat(root.Descendants("POU"));

        foreach (var pouElement in pouElements)
        {
            var program = new ProgramInfo
            {
                Name = pouElement.Attribute("name")?.Value ?? "Unknown",
                Type = DeterminePouType(pouElement),
                Language = DeterminePouLanguage(pouElement),
                Description = pouElement.Element("documentation")?.Value ??
                             pouElement.Element("comment")?.Value,
                Routines = ParsePouActions(pouElement),
                LocalTags = ParsePouVariables(pouElement),
                Properties = new Dictionary<string, string>()
            };

            // Extract additional properties
            var properties = pouElement.Elements("Property");
            foreach (var prop in properties)
            {
                var name = prop.Attribute("Name")?.Value;
                var value = prop.Value;
                if (!string.IsNullOrEmpty(name))
                {
                    program.Properties[name] = value;
                }
            }

            // Count networks if FBD/LAD
            if (program.Language == "FBD" || program.Language == "LAD")
            {
                var networkCount = pouElement.Descendants("network").Count() +
                                  pouElement.Descendants("Network").Count();
                if (networkCount > 0)
                {
                    program.Properties["NetworkCount"] = networkCount.ToString();
                }
            }

            programs.Add(program);
        }

        // Also parse data types as programs
        var dutElements = root.Descendants("dataType").Concat(root.Descendants("DUT"));
        foreach (var dutElement in dutElements)
        {
            var dut = new ProgramInfo
            {
                Name = dutElement.Attribute("name")?.Value ?? "Unknown",
                Type = "DUT",
                Language = "Declaration",
                Description = dutElement.Element("comment")?.Value,
                Properties = new Dictionary<string, string>
                {
                    ["DataType"] = "true"
                }
            };
            programs.Add(dut);
        }

        return programs;
    }

    private string DeterminePouType(XElement pouElement)
    {
        var pouType = pouElement.Attribute("pouType")?.Value ??
                     pouElement.Attribute("type")?.Value;

        if (string.IsNullOrEmpty(pouType))
        {
            // Try to determine from content
            if (pouElement.Element("interface")?.Element("returnType") != null)
                return "Function";

            var name = pouElement.Attribute("name")?.Value?.ToUpper() ?? "";
            if (name.StartsWith("FB_") || name.EndsWith("_FB"))
                return "FunctionBlock";
            if (name.StartsWith("FC_") || name.EndsWith("_FC") || name.StartsWith("F_"))
                return "Function";

            return "Program";
        }

        // Return pouType as-is to match what's in the XML
        return pouType;
    }

    private string DeterminePouLanguage(XElement pouElement)
    {
        // Check for explicit language attribute
        var language = pouElement.Attribute("language")?.Value;
        if (!string.IsNullOrEmpty(language))
            return language.ToUpper();

        // Check implementation element
        var implementation = pouElement.Element("implementation") ??
                            pouElement.Element("body");

        if (implementation != null)
        {
            if (implementation.Element("ST") != null) return "ST";
            if (implementation.Element("IL") != null) return "IL";
            if (implementation.Element("LD") != null || implementation.Element("LAD") != null) return "LAD";
            if (implementation.Element("FBD") != null) return "FBD";
            if (implementation.Element("SFC") != null) return "SFC";
            if (implementation.Element("CFC") != null) return "CFC";
        }

        // Try to detect from content
        var bodyText = pouElement.Element("body")?.Value ?? "";
        if (bodyText.Contains("END_IF") || bodyText.Contains("END_FOR") || bodyText.Contains(":="))
            return "ST";
        if (bodyText.Contains("LD ") || bodyText.Contains("ST ") || bodyText.Contains("JMP "))
            return "IL";

        return "ST"; // Default
    }

    private IReadOnlyList<RoutineInfo> ParsePouActions(XElement pouElement)
    {
        var routines = new List<RoutineInfo>();

        // Main implementation
        var implementation = pouElement.Element("implementation") ?? pouElement.Element("body");
        if (implementation != null)
        {
            var mainRoutine = new RoutineInfo
            {
                Name = "Main",
                Type = "Main",
                Language = DeterminePouLanguage(pouElement),
                ProgramName = pouElement.Attribute("name")?.Value,
                NetworkCount = implementation.Descendants("network").Count() +
                              implementation.Descendants("Network").Count()
            };
            routines.Add(mainRoutine);
        }

        // Parse actions
        var actions = pouElement.Descendants("action").Concat(pouElement.Descendants("Action"));
        foreach (var action in actions)
        {
            var routine = new RoutineInfo
            {
                Name = action.Attribute("name")?.Value ?? "Action",
                Type = "Action",
                Language = DeterminePouLanguage(action),
                ProgramName = pouElement.Attribute("name")?.Value,
                Description = action.Element("documentation")?.Value,
                NetworkCount = action.Descendants("network").Count() +
                              action.Descendants("Network").Count()
            };
            routines.Add(routine);
        }

        // Parse methods (for function blocks)
        var methods = pouElement.Descendants("method").Concat(pouElement.Descendants("Method"));
        foreach (var method in methods)
        {
            var routine = new RoutineInfo
            {
                Name = method.Attribute("name")?.Value ?? "Method",
                Type = "Method",
                Language = DeterminePouLanguage(method),
                ProgramName = pouElement.Attribute("name")?.Value,
                Description = method.Element("documentation")?.Value
            };
            routines.Add(routine);
        }

        // Parse transitions (for SFC)
        var transitions = pouElement.Descendants("transition").Concat(pouElement.Descendants("Transition"));
        foreach (var transition in transitions)
        {
            var routine = new RoutineInfo
            {
                Name = transition.Attribute("name")?.Value ?? "Transition",
                Type = "Transition",
                Language = "SFC",
                ProgramName = pouElement.Attribute("name")?.Value
            };
            routines.Add(routine);
        }

        return routines;
    }

    private IReadOnlyList<TagInfo> ParsePouVariables(XElement pouElement)
    {
        var tags = new List<TagInfo>();

        var interfaceElement = pouElement.Element("interface");
        if (interfaceElement != null)
        {
            // Parse different variable sections
            ParseVariableSection(interfaceElement.Element("localVars"), "VAR", pouElement.Attribute("name")?.Value ?? "", tags);
            ParseVariableSection(interfaceElement.Element("inputVars"), "VAR_INPUT", pouElement.Attribute("name")?.Value ?? "", tags);
            ParseVariableSection(interfaceElement.Element("outputVars"), "VAR_OUTPUT", pouElement.Attribute("name")?.Value ?? "", tags);
            ParseVariableSection(interfaceElement.Element("inOutVars"), "VAR_IN_OUT", pouElement.Attribute("name")?.Value ?? "", tags);
            ParseVariableSection(interfaceElement.Element("externalVars"), "VAR_EXTERNAL", pouElement.Attribute("name")?.Value ?? "", tags);
            ParseVariableSection(interfaceElement.Element("globalVars"), "VAR_GLOBAL", pouElement.Attribute("name")?.Value ?? "", tags);
            ParseVariableSection(interfaceElement.Element("tempVars"), "VAR_TEMP", pouElement.Attribute("name")?.Value ?? "", tags);
        }

        return tags;
    }

    private void ParseVariableSection(XElement? section, string usage, string scope, List<TagInfo> tags)
    {
        if (section == null)
            return;

        var variables = section.Elements("variable").Concat(section.Elements("Variable"));
        foreach (var variable in variables)
        {
            // Normalize usage to match test expectations
            var normalizedUsage = usage switch
            {
                "VAR_INPUT" => "Input",
                "VAR_OUTPUT" => "Output",
                "VAR_IN_OUT" => "InOut",
                _ => usage
            };

            var tag = new TagInfo
            {
                Name = variable.Attribute("name")?.Value ?? "Unknown",
                DataType = ParseDataType(variable),
                Scope = scope,
                Usage = normalizedUsage,
                Description = variable.Element("documentation")?.Value ??
                             variable.Element("comment")?.Value,
                InitialValue = variable.Element("initialValue")?.Value,
                Address = variable.Attribute("address")?.Value,
                Properties = new Dictionary<string, string>()
            };

            // Check if it's an I/O variable
            if (!string.IsNullOrEmpty(tag.Address))
            {
                tag.IsIoTag = tag.Address.StartsWith("%I") || tag.Address.StartsWith("%Q");
            }

            // Extract attributes
            var attributes = variable.Elements("attribute");
            foreach (var attr in attributes)
            {
                var name = attr.Attribute("name")?.Value;
                var value = attr.Value;
                if (!string.IsNullOrEmpty(name))
                {
                    tag.Properties[name] = value;
                }
            }

            tags.Add(tag);
        }
    }

    private string ParseDataType(XElement variable)
    {
        // Direct type element
        var typeElement = variable.Element("type");
        if (typeElement != null)
        {
            // Check for simple type elements by name
            if (typeElement.Element("BOOL") != null)
                return "BOOL";
            if (typeElement.Element("INT") != null)
                return "INT";
            if (typeElement.Element("DINT") != null)
                return "DINT";
            if (typeElement.Element("REAL") != null)
                return "REAL";
            if (typeElement.Element("STRING") != null)
                return "STRING";

            // Simple type with value
            var simpleType = typeElement.Element("simple")?.Value;
            if (!string.IsNullOrEmpty(simpleType))
                return simpleType;

            // Array type
            var arrayElement = typeElement.Element("array");
            if (arrayElement != null)
            {
                var baseType = arrayElement.Element("baseType")?.Value ?? "Unknown";
                var dimensions = arrayElement.Elements("dimension").Select(d => d.Attribute("upper")?.Value).ToList();
                if (dimensions.Any())
                {
                    return $"ARRAY[{string.Join(",", dimensions)}] OF {baseType}";
                }
                return $"ARRAY OF {baseType}";
            }

            // Derived type
            var derivedElement = typeElement.Element("derived");
            if (derivedElement != null)
            {
                return derivedElement.Attribute("name")?.Value ?? "Unknown";
            }

            // Pointer type
            var pointerElement = typeElement.Element("pointer");
            if (pointerElement != null)
            {
                var baseType = pointerElement.Element("baseType")?.Value ?? "Unknown";
                return $"POINTER TO {baseType}";
            }

            // String with length
            var stringElement = typeElement.Element("string");
            if (stringElement != null)
            {
                var length = stringElement.Attribute("length")?.Value;
                return string.IsNullOrEmpty(length) ? "STRING" : $"STRING({length})";
            }
        }

        // Type attribute
        return variable.Attribute("type")?.Value ?? "Unknown";
    }

    private IReadOnlyList<TagInfo> ParseGlobalVariables(XElement root)
    {
        var globalTags = new List<TagInfo>();

        // Find global variable lists
        var gvlElements = root.Descendants("globalVars")
            .Concat(root.Descendants("GVL"))
            .Concat(root.Descendants("data").Where(d => d.Attribute("name")?.Value?.Contains("Global") == true));

        foreach (var gvlElement in gvlElements)
        {
            var gvlName = gvlElement.Attribute("name")?.Value ?? "Global";
            ParseVariableSection(gvlElement, "VAR_GLOBAL", gvlName, globalTags);
        }

        // Find persistent variables
        var persistentVars = root.Descendants("data")
            .Where(d => d.Attribute("name")?.Value?.Contains("Persistent") == true);

        foreach (var pvElement in persistentVars)
        {
            ParseVariableSection(pvElement, "VAR_GLOBAL_PERSISTENT", "Persistent", globalTags);
        }

        return globalTags;
    }

    private IReadOnlyList<IoModuleInfo> ParseIoConfiguration(XElement root)
    {
        var modules = new List<IoModuleInfo>();

        // Find I/O configuration
        var ioElements = root.Descendants("Device")
            .Concat(root.Descendants("Module"))
            .Concat(root.Descendants("addData").Where(d =>
                d.Element("data")?.Attribute("name")?.Value?.Contains("I/O") == true ||
                d.Element("data")?.Attribute("name")?.Value?.Contains("IO") == true));

        foreach (var ioElement in ioElements)
        {
            var deviceType = ioElement.Attribute("deviceType")?.Value ??
                           ioElement.Attribute("moduleType")?.Value;

            if (!string.IsNullOrEmpty(deviceType) && IsIoDevice(deviceType))
            {
                var module = new IoModuleInfo
                {
                    Name = ioElement.Attribute("name")?.Value ?? deviceType,
                    Type = DetermineIoModuleType(deviceType),
                    CatalogNumber = deviceType,
                    Vendor = ExtractVendor(ioElement),
                    Location = ioElement.Attribute("id")?.Value,
                    Properties = new Dictionary<string, string>()
                };

                // Count channels
                var channels = ioElement.Descendants("Channel").Count() +
                              ioElement.Descendants("channel").Count();
                if (channels > 0)
                {
                    module.ChannelCount = channels;
                }

                // Extract configuration
                var configElements = ioElement.Elements("Parameter");
                foreach (var config in configElements)
                {
                    var name = config.Attribute("name")?.Value;
                    var value = config.Attribute("value")?.Value ?? config.Value;
                    if (!string.IsNullOrEmpty(name))
                    {
                        module.Configuration[name] = value;
                    }
                }

                modules.Add(module);
            }
        }

        // Also check for I/O mapping
        var mappingElements = root.Descendants("IoMapping").Concat(root.Descendants("Mapping"));
        foreach (var mapping in mappingElements)
        {
            var module = new IoModuleInfo
            {
                Name = mapping.Attribute("name")?.Value ?? "I/O Mapping",
                Type = "I/O Mapping",
                Properties = new Dictionary<string, string>
                {
                    ["MappingType"] = mapping.Attribute("type")?.Value ?? ""
                }
            };

            // Count mapped variables
            var mappedVars = mapping.Descendants("Map").Count() +
                           mapping.Descendants("map").Count();
            if (mappedVars > 0)
            {
                module.ChannelCount = mappedVars;
            }

            modules.Add(module);
        }

        return modules;
    }

    private bool IsIoDevice(string deviceType)
    {
        var ioKeywords = new[] { "Input", "Output", "I/O", "IO", "DI", "DO", "AI", "AO", "Encoder", "Drive" };
        return ioKeywords.Any(kw => deviceType.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }

    private string DetermineIoModuleType(string deviceType)
    {
        var upper = deviceType.ToUpper();

        if (upper.Contains("DIGITAL") && upper.Contains("INPUT")) return "Digital Input";
        if (upper.Contains("DIGITAL") && upper.Contains("OUTPUT")) return "Digital Output";
        if (upper.Contains("ANALOG") && upper.Contains("INPUT")) return "Analog Input";
        if (upper.Contains("ANALOG") && upper.Contains("OUTPUT")) return "Analog Output";
        if (upper.Contains("ENCODER")) return "Encoder";
        if (upper.Contains("DRIVE")) return "Drive";
        if (upper.Contains("SAFETY")) return "Safety I/O";
        if (upper.Contains("COMMUNICATION")) return "Communication";

        return "I/O Module";
    }

    private string ExtractVendor(XElement element)
    {
        var vendor = element.Element("DeviceIdentification")?.Element("Vendor")?.Value ??
                    element.Attribute("vendor")?.Value;

        if (string.IsNullOrEmpty(vendor))
        {
            // Try to guess from device type
            var deviceType = element.Attribute("deviceType")?.Value ?? "";
            if (deviceType.Contains("Beckhoff", StringComparison.OrdinalIgnoreCase))
                return "Beckhoff";
            if (deviceType.Contains("Wago", StringComparison.OrdinalIgnoreCase))
                return "Wago";
            if (deviceType.Contains("Phoenix", StringComparison.OrdinalIgnoreCase))
                return "Phoenix Contact";
            if (deviceType.Contains("IFM", StringComparison.OrdinalIgnoreCase))
                return "IFM";
        }

        return vendor ?? "CODESYS";
    }

    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    private static void StripNamespaces(XElement root)
    {
        // Iterative: recursion overflowed the stack (uncatchable) on deeply nested XML.
        foreach (var element in root.DescendantsAndSelf().ToList())
        {
            element.Name = element.Name.LocalName;

            var attributes = element.Attributes().ToList();
            element.RemoveAttributes();
            foreach (var attr in attributes)
            {
                // Drop xsi:* so xsi:type can't overwrite a real "type" attribute.
                if (!attr.IsNamespaceDeclaration && attr.Name.Namespace != Xsi)
                {
                    element.SetAttributeValue(attr.Name.LocalName, attr.Value);
                }
            }
        }
    }
}