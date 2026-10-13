using System.Text.RegularExpressions;
using System.Xml.Linq;
using PlcInfo.Models;

namespace PlcInfo.Parsing;

/// <summary>
/// Parser for Beckhoff TwinCAT 3 project files (.tsproj).
/// </summary>
public class TwinCatParser : IPlcProjectParser
{
    /// <inheritdoc/>
    public bool CanParse(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLower();
        if (extension == ".tsproj" || extension == ".tspproj" ||
            extension == ".tpy" || extension == ".tszip" || extension == ".tnzip")
            return true;

        if (!File.Exists(filePath))
            return false;

        // Check if it's a TwinCAT XML file
        if (extension == ".xml")
        {
            try
            {
                var firstLines = File.ReadLines(filePath).Take(5).ToList();
                return firstLines.Any(line =>
                    line.Contains("TcSmProject", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("TwinCAT", StringComparison.OrdinalIgnoreCase));
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
            throw new InvalidOperationException("Invalid TwinCAT project file: no root element");

        var projectName = ExtractProjectName(root, filePath);
        var controller = ExtractControllerInfo(root, projectName);
        var tasks = ParseTasks(root);
        var programs = ParsePlcProjects(root, filePath);
        var tags = ParseGlobalVariables(root);
        var modules = ParseIoConfiguration(root);

        return new PlcProject
        {
            Vendor = "Beckhoff",
            Format = "TwinCAT",
            FilePath = filePath,
            Controller = controller,
            Tasks = tasks,
            Programs = programs,
            Tags = tags,
            IoModules = modules,
            LastModified = File.GetLastWriteTime(filePath),
            Metadata = new Dictionary<string, string>
            {
                ["TwinCATVersion"] = root.Attribute("TcVersion")?.Value ?? "3.1",
                ["TcSmVersion"] = root.Attribute("TcSmVersion")?.Value ?? "",
                ["ProjectGuid"] = root.Element("Project")?.Attribute("ProjectGuid")?.Value ?? "",
                ["ProductVersion"] = root.Attribute("ProductVersion")?.Value ?? ""
            }
        };
    }

    private string ExtractProjectName(XElement root, string filePath)
    {
        return root.Element("Project")?.Attribute("Name")?.Value ??
               root.Attribute("Name")?.Value ??
               Path.GetFileNameWithoutExtension(filePath);
    }

    private ControllerInfo ExtractControllerInfo(XElement root, string projectName)
    {
        // Look for Device element first, then fall back to project name
        var deviceElement = root.Descendants("Device").FirstOrDefault();
        var controllerName = deviceElement?.Attribute("Name")?.Value ?? projectName;

        var controller = new ControllerInfo
        {
            Name = controllerName,
            Vendor = "Beckhoff",
            Type = "TwinCAT 3 Runtime",
            Properties = new Dictionary<string, string>()
        };

        // Check for CPU type in Device/Module
        if (deviceElement != null)
        {
            var cpuModule = deviceElement.Element("Module");
            if (cpuModule != null)
            {
                var typeProperty = cpuModule.Descendants("Property")
                    .FirstOrDefault(p => p.Attribute("Name")?.Value == "Type");
                if (typeProperty != null)
                {
                    controller.Type = typeProperty.Attribute("Value")?.Value ?? controller.Type;
                }

                var versionProperty = cpuModule.Descendants("Property")
                    .FirstOrDefault(p => p.Attribute("Name")?.Value == "Version");

                if (versionProperty != null)
                {
                    controller.FirmwareRevision = versionProperty.Attribute("Value")?.Value ?? controller.FirmwareRevision;
                }
            }
        }

        // Find system configuration
        var systemElement = root.Descendants("System").FirstOrDefault() ??
                           root.Descendants("TcSmProject").FirstOrDefault();

        if (systemElement != null)
        {
            // Extract target system info
            var targetElement = systemElement.Element("TargetSystem") ??
                               systemElement.Descendants("Target").FirstOrDefault();

            if (targetElement != null)
            {
                controller.Type = targetElement.Attribute("Type")?.Value ?? "TwinCAT 3 Runtime";
                controller.CpuType = targetElement.Attribute("CpuType")?.Value ??
                                    targetElement.Element("Cpu")?.Value;
                controller.FirmwareRevision = targetElement.Attribute("Version")?.Value ?? "";

                // AMS NetId
                var amsNetId = targetElement.Attribute("AmsNetId")?.Value ??
                               targetElement.Element("AmsNetId")?.Value;
                if (!string.IsNullOrEmpty(amsNetId))
                {
                    controller.Properties["AmsNetId"] = amsNetId;
                }
            }

            // Extract routes/network info
            var routeCount = systemElement.Descendants("Route").Count();
            if (routeCount > 0)
            {
                controller.NetworkCount = routeCount;
            }
        }

        // Extract settings
        var settingsElement = root.Descendants("Settings").FirstOrDefault();
        if (settingsElement != null)
        {
            var maxCpus = settingsElement.Element("MaxCpus")?.Value;
            if (!string.IsNullOrEmpty(maxCpus))
            {
                controller.Properties["MaxCpus"] = maxCpus;
            }

            var routerMemory = settingsElement.Element("RouterMemory")?.Value;
            if (!string.IsNullOrEmpty(routerMemory))
            {
                controller.Properties["RouterMemory"] = routerMemory;
            }
        }

        return controller;
    }

    private IReadOnlyList<TaskInfo> ParseTasks(XElement root)
    {
        var tasks = new List<TaskInfo>();

        // Find task configuration in TwinCAT system
        var taskElements = root.Descendants("Task")
            .Concat(root.Descendants("TcTask"))
            .Concat(root.Descendants("PlcTask"));

        foreach (var taskElement in taskElements)
        {
            var task = new TaskInfo
            {
                Name = taskElement.Attribute("Name")?.Value ??
                       taskElement.Element("Name")?.Value ?? "Task",
                Type = DetermineTaskType(taskElement),
                Priority = ParseTaskPriority(taskElement),
                Rate = ParseCycleTime(taskElement),
                IsEnabled = !bool.TryParse(taskElement.Attribute("Disabled")?.Value, out var disabled) || !disabled,
                Properties = new Dictionary<string, string>()
            };

            // Extract CPU affinity
            var cpuAffinity = taskElement.Element("CpuAffinity")?.Value ??
                             taskElement.Attribute("CpuAffinity")?.Value;
            if (!string.IsNullOrEmpty(cpuAffinity))
            {
                task.Properties["CpuAffinity"] = cpuAffinity;
            }

            // Extract task context
            var context = taskElement.Element("Context")?.Attribute("Id")?.Value;
            if (!string.IsNullOrEmpty(context))
            {
                task.Properties["ContextId"] = context;
            }

            // Find linked POUs
            var linkedPous = taskElement.Descendants("Object")
                .Where(o => o.Attribute("Type")?.Value == "POU" ||
                           o.Parent?.Name.LocalName == "Implementation")
                .Select(o => o.Attribute("Name")?.Value ?? o.Value)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();

            task.ProgramNames = linkedPous;

            // Watchdog settings
            var watchdog = taskElement.Element("Watchdog");
            if (watchdog != null)
            {
                if (double.TryParse(watchdog.Attribute("Time")?.Value, out var watchdogTime))
                {
                    task.WatchdogTimeout = watchdogTime;
                }
            }

            tasks.Add(task);
        }

        // If no tasks found, look in PLC projects
        if (tasks.Count == 0)
        {
            var plcProjects = root.Descendants("PlcProject").Concat(root.Descendants("Plc"));
            foreach (var plcProject in plcProjects)
            {
                var projectTasks = plcProject.Descendants("Task");
                foreach (var taskElement in projectTasks)
                {
                    var task = new TaskInfo
                    {
                        Name = taskElement.Attribute("Name")?.Value ?? "PlcTask",
                        Type = "Cyclic",
                        Priority = int.TryParse(taskElement.Attribute("Priority")?.Value, out var p) ? p : 20,
                        Properties = new Dictionary<string, string>
                        {
                            ["PlcProject"] = plcProject.Attribute("Name")?.Value ?? ""
                        }
                    };

                    tasks.Add(task);
                }
            }
        }

        return tasks;
    }

    private string DetermineTaskType(XElement taskElement)
    {
        var type = taskElement.Attribute("Type")?.Value ??
                  taskElement.Element("Type")?.Value;

        if (string.IsNullOrEmpty(type))
        {
            // Check for cycle time to determine if cyclic
            if (taskElement.Element("CycleTime") != null ||
                taskElement.Attribute("CycleTime") != null)
            {
                return "Cyclic";
            }

            return "Freewheeling";
        }

        return type switch
        {
            "1" => "Cyclic",
            "2" => "Freewheeling",
            "3" => "Event",
            "4" => "External",
            _ => type
        };
    }

    private int ParseTaskPriority(XElement taskElement)
    {
        var priorityStr = taskElement.Attribute("Priority")?.Value ??
                         taskElement.Element("Priority")?.Value;

        if (int.TryParse(priorityStr, out var priority))
            return priority;

        return 20; // Default TwinCAT priority
    }

    private double? ParseCycleTime(XElement taskElement)
    {
        var cycleTimeStr = taskElement.Attribute("CycleTime")?.Value ??
                          taskElement.Element("CycleTime")?.Value;

        if (string.IsNullOrEmpty(cycleTimeStr))
            return null;

        // TwinCAT cycle time is usually in 100ns units
        if (long.TryParse(cycleTimeStr, out var cycleTime100ns))
        {
            // Convert from 100ns to milliseconds
            return cycleTime100ns / 10000.0;
        }

        // Try parsing as direct milliseconds if it contains decimal point
        if (cycleTimeStr.Contains('.'))
        {
            if (double.TryParse(cycleTimeStr, out var ms))
                return ms;
        }

        return null;
    }

    private IReadOnlyList<ProgramInfo> ParsePlcProjects(XElement root, string projectFilePath)
    {
        var programs = new List<ProgramInfo>();
        var projectDir = Path.GetDirectoryName(projectFilePath) ?? "";

        // First check for POUs directly in this document
        var pous = root.Descendants("POU");
        if (pous.Any())
        {
            programs.AddRange(ParsePousDirectly(pous));
        }

        // Then find PLC project references
        var plcProjects = root.Descendants("PlcProject")
            .Concat(root.Descendants("Plc"))
            .Concat(root.Descendants("Project").Where(p =>
                p.Attribute("Type")?.Value == "PlcProject" ||
                p.Attribute("SubType")?.Value?.Contains("TwinCAT") == true));

        foreach (var plcProject in plcProjects)
        {
            // Real .tsproj files reference the PLC project via PrjFilePath.
            var plcProjectFile = plcProject.Attribute("File")?.Value ??
                                plcProject.Attribute("PrjFilePath")?.Value ??
                                plcProject.Element("File")?.Value;

            if (!string.IsNullOrEmpty(plcProjectFile))
            {
                // Parse the referenced PLC project file
                var plcPath = ResolveProjectReference(projectDir, plcProjectFile);

                if (plcPath != null && File.Exists(plcPath))
                {
                    try
                    {
                        var plcPrograms = ParsePlcProjectFile(plcPath);
                        programs.AddRange(plcPrograms);
                    }
                    catch { }
                }
            }

            // Also parse inline POUs
            var inlinePous = ParsePousFromElement(plcProject);
            programs.AddRange(inlinePous);
        }

        // Look for PLC folders in project directory
        if (programs.Count == 0)
        {
            var plcFolders = new[] { "_Boot", "PLC", "PlcProject", "Plc1" };
            foreach (var folder in plcFolders)
            {
                var plcDir = Path.Combine(projectDir, folder);
                if (Directory.Exists(plcDir))
                {
                    var plcFiles = Directory.GetFiles(plcDir, "*.plcproj", SearchOption.AllDirectories)
                        .Concat(Directory.GetFiles(plcDir, "*.xti", SearchOption.AllDirectories));

                    foreach (var plcFile in plcFiles)
                    {
                        try
                        {
                            var plcPrograms = ParsePlcProjectFile(plcFile);
                            programs.AddRange(plcPrograms);
                        }
                        catch { }
                    }
                }
            }
        }

        return programs;
    }

    private List<ProgramInfo> ParsePlcProjectFile(string plcPath)
    {
        var programs = new List<ProgramInfo>();

        try
        {
            var content = File.ReadAllText(plcPath);
            var doc = XDocument.Parse(content);

            var pous = ParsePousFromElement(doc.Root);
            programs.AddRange(pous);
        }
        catch { }

        return programs;
    }

    private List<ProgramInfo> ParsePousFromElement(XElement? element)
    {
        var programs = new List<ProgramInfo>();

        if (element == null)
            return programs;

        // Find POUs
        var pouElements = element.Descendants("POU")
            .Concat(element.Descendants("Pou"))
            .Concat(element.Descendants("Object").Where(o =>
                o.Attribute("Type")?.Value == "POU" ||
                o.Attribute("ObjectType")?.Value == "POU"));

        foreach (var pouElement in pouElements)
        {
            var program = new ProgramInfo
            {
                Name = pouElement.Attribute("Name")?.Value ??
                       pouElement.Element("Name")?.Value ?? "Unknown",
                Type = DeterminePouType(pouElement),
                Language = DeterminePouLanguage(pouElement),
                Description = pouElement.Element("Declaration")?.Element("Comment")?.Value,
                Routines = ParsePouMethods(pouElement),
                LocalTags = ParsePouVariables(pouElement),
                Properties = new Dictionary<string, string>()
            };

            // Extract GUID
            var guid = pouElement.Attribute("Id")?.Value ??
                      pouElement.Attribute("GUID")?.Value;
            if (!string.IsNullOrEmpty(guid))
            {
                program.Properties["GUID"] = guid;
            }

            programs.Add(program);
        }

        // Parse DUTs
        var dutElements = element.Descendants("DUT")
            .Concat(element.Descendants("DataType"));

        foreach (var dutElement in dutElements)
        {
            var dut = new ProgramInfo
            {
                Name = dutElement.Attribute("Name")?.Value ?? "Unknown",
                Type = "DUT",
                Language = "Declaration",
                Description = dutElement.Element("Declaration")?.Element("Comment")?.Value,
                Properties = new Dictionary<string, string>
                {
                    ["DataType"] = "true",
                    ["DutType"] = dutElement.Attribute("Type")?.Value ?? "Structure"
                }
            };

            programs.Add(dut);
        }

        // Parse GVLs
        var gvlElements = element.Descendants("GVL")
            .Concat(element.Descendants("GlobalVariableList"));

        foreach (var gvlElement in gvlElements)
        {
            var gvl = new ProgramInfo
            {
                Name = gvlElement.Attribute("Name")?.Value ?? "GVL",
                Type = "GVL",
                Language = "Declaration",
                LocalTags = ParseGvlVariables(gvlElement),
                Properties = new Dictionary<string, string>
                {
                    ["GlobalVariableList"] = "true"
                }
            };

            programs.Add(gvl);
        }

        return programs;
    }

    private string DeterminePouType(XElement pouElement)
    {
        var kind = pouElement.Attribute("Kind")?.Value ??
                  pouElement.Attribute("PouType")?.Value ??
                  pouElement.Element("Type")?.Value;

        if (!string.IsNullOrEmpty(kind))
        {
            return kind switch
            {
                "0" => "Program",
                "1" => "FunctionBlock",
                "2" => "Function",
                "PROGRAM" => "Program",
                "FUNCTION_BLOCK" => "FunctionBlock",
                "FUNCTION" => "Function",
                _ => kind
            };
        }

        // Try to determine from declaration
        var declaration = pouElement.Element("Declaration")?.Value ?? "";
        if (declaration.Contains("PROGRAM", StringComparison.OrdinalIgnoreCase))
            return "Program";
        if (declaration.Contains("FUNCTION_BLOCK", StringComparison.OrdinalIgnoreCase))
            return "Function Block";
        if (declaration.Contains("FUNCTION", StringComparison.OrdinalIgnoreCase))
            return "Function";

        return "Program";
    }

    private string DeterminePouLanguage(XElement pouElement)
    {
        var implementation = pouElement.Element("Implementation");
        if (implementation != null)
        {
            var language = implementation.Attribute("Language")?.Value;
            if (!string.IsNullOrEmpty(language))
                return language;

            // Check for ST content
            var stElement = implementation.Element("ST");
            if (stElement != null)
                return "ST";

            // Check for declaration-only
            if (implementation.Elements().Count() == 0)
                return "Declaration";
        }

        // Default for TwinCAT
        return "ST";
    }

    private IReadOnlyList<RoutineInfo> ParsePouMethods(XElement pouElement)
    {
        var routines = new List<RoutineInfo>();

        // Main implementation
        var implementation = pouElement.Element("Implementation");
        if (implementation != null && implementation.Element("ST") != null)
        {
            var mainRoutine = new RoutineInfo
            {
                Name = "Main",
                Type = "Main",
                Language = "ST",
                ProgramName = pouElement.Attribute("Name")?.Value
            };
            routines.Add(mainRoutine);
        }

        // Parse methods
        var methods = pouElement.Descendants("Method");
        foreach (var method in methods)
        {
            var routine = new RoutineInfo
            {
                Name = method.Attribute("Name")?.Value ?? "Method",
                Type = "Method",
                Language = DeterminePouLanguage(method),
                ProgramName = pouElement.Attribute("Name")?.Value,
                Description = method.Element("Declaration")?.Element("Comment")?.Value
            };
            routines.Add(routine);
        }

        // Parse properties
        var properties = pouElement.Descendants("Property");
        foreach (var property in properties)
        {
            var routine = new RoutineInfo
            {
                Name = property.Attribute("Name")?.Value ?? "Property",
                Type = "Property",
                Language = "ST",
                ProgramName = pouElement.Attribute("Name")?.Value
            };
            routines.Add(routine);
        }

        // Parse actions
        var actions = pouElement.Descendants("Action");
        foreach (var action in actions)
        {
            var routine = new RoutineInfo
            {
                Name = action.Attribute("Name")?.Value ?? "Action",
                Type = "Action",
                Language = DeterminePouLanguage(action),
                ProgramName = pouElement.Attribute("Name")?.Value
            };
            routines.Add(routine);
        }

        return routines;
    }

    private IReadOnlyList<TagInfo> ParsePouVariables(XElement pouElement)
    {
        var tags = new List<TagInfo>();

        var declaration = pouElement.Element("Declaration");
        if (declaration != null)
        {
            var declarationText = declaration.Value;
            tags.AddRange(ParseDeclarationText(declarationText, pouElement.Attribute("Name")?.Value ?? ""));
        }

        return tags;
    }

    private IReadOnlyList<TagInfo> ParseGvlVariables(XElement gvlElement)
    {
        var tags = new List<TagInfo>();

        var declaration = gvlElement.Element("Declaration");
        if (declaration != null)
        {
            var declarationText = declaration.Value;
            tags.AddRange(ParseDeclarationText(declarationText, "Global"));
        }

        return tags;
    }

    private static readonly Regex VarSectionHeader = new(
        @"^VAR(?<kind>_INPUT|_OUTPUT|_IN_OUT|_GLOBAL|_TEMP|_STAT|_INST|_EXTERNAL|_CONFIG)?(?<quals>(\s+(CONSTANT|RETAIN|PERSISTENT|NON_RETAIN))*)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex EndVar = new(
        @"^END_VAR\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Resolves a project-relative reference (TwinCAT writes Windows separators) and
    /// rejects anything that escapes the project directory, such as "../x" or rooted paths.
    /// </summary>
    internal static string? ResolveProjectReference(string projectDir, string reference)
    {
        var normalized = reference.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
            return null;

        var root = Path.GetFullPath(projectDir);
        var full = Path.GetFullPath(Path.Combine(root, normalized));
        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return full.StartsWith(rootWithSep, StringComparison.Ordinal) ? full : null;
    }

    internal static List<TagInfo> ParseDeclarationText(string declarationText, string scope)
    {
        var tags = new List<TagInfo>();
        var cleaned = StripCommentsAndPragmas(declarationText);
        string currentSection = "";
        var pending = new System.Text.StringBuilder();

        foreach (var rawLine in cleaned.Split('\n'))
        {
            var trimmed = rawLine.Trim();
            if (trimmed.Length == 0)
                continue;

            var header = VarSectionHeader.Match(trimmed);
            if (header.Success)
            {
                var kind = header.Groups["kind"].Value.ToUpperInvariant();
                var persistent = header.Groups["quals"].Value.Contains("PERSISTENT", StringComparison.OrdinalIgnoreCase);
                currentSection = kind.Length > 0 ? "VAR" + kind : persistent ? "VAR_PERSISTENT" : "VAR";
                pending.Clear();
                continue;
            }

            if (EndVar.IsMatch(trimmed))
            {
                currentSection = "";
                pending.Clear();
                continue;
            }

            if (currentSection.Length == 0)
                continue;

            // Declarations may span lines; complete statements end with ';'.
            pending.Append(trimmed).Append(' ');
            foreach (var statement in TakeCompleteStatements(pending))
            {
                tags.AddRange(ParseDeclarationStatement(statement, currentSection, scope));
            }
        }

        return tags;
    }

    private static IEnumerable<TagInfo> ParseDeclarationStatement(string statement, string section, string scope)
    {
        var colonIndex = IndexOutsideStrings(statement, ':', skipAssign: true);
        if (colonIndex <= 0)
            yield break;

        var namePart = statement[..colonIndex].Trim();
        var typePart = statement[(colonIndex + 1)..].Trim();

        string? initialValue = null;
        var assignIndex = typePart.IndexOf(":=", StringComparison.Ordinal);
        if (assignIndex >= 0)
        {
            initialValue = typePart[(assignIndex + 2)..].Trim();
            typePart = typePart[..assignIndex].Trim();
        }

        // Direct address: "bIn AT %IX0.0 : BOOL"
        string? address = null;
        var atMatch = Regex.Match(namePart, @"\s+AT\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (atMatch.Success)
        {
            address = namePart[(atMatch.Index + atMatch.Length)..].Trim();
            namePart = namePart[..atMatch.Index].Trim();
        }

        var usage = section switch
        {
            "VAR_INPUT" => "Input",
            "VAR_OUTPUT" => "Output",
            "VAR_IN_OUT" => "InOut",
            _ => section
        };

        // "a, b, c : BOOL;" declares three variables.
        foreach (var name in namePart.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return new TagInfo
            {
                Name = name,
                DataType = typePart,
                Scope = scope,
                Usage = usage,
                InitialValue = initialValue,
                Address = address,
                IsIoTag = address?.StartsWith("%I", StringComparison.OrdinalIgnoreCase) == true ||
                          address?.StartsWith("%Q", StringComparison.OrdinalIgnoreCase) == true
            };
        }
    }

    /// <summary>
    /// Removes (* *) and // comments and {pragmas} that are outside string literals,
    /// keeping newlines so line structure survives.
    /// </summary>
    internal static string StripCommentsAndPragmas(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (c == '\'' || c == '"')
            {
                // Copy string literal verbatim ('' / $' escapes keep us inside it).
                var quote = c;
                sb.Append(c);
                i++;
                while (i < text.Length)
                {
                    var sc = text[i];
                    sb.Append(sc);
                    i++;
                    if (sc == '$' && i < text.Length) { sb.Append(text[i]); i++; continue; }
                    if (sc == quote) break;
                }
                continue;
            }

            if (c == '(' && next == '*')
            {
                var end = text.IndexOf("*)", i + 2, StringComparison.Ordinal);
                var stop = end < 0 ? text.Length : end + 2;
                AppendNewlines(sb, text, i, stop);
                i = stop;
                continue;
            }

            if (c == '/' && next == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }

            if (c == '{')
            {
                var end = text.IndexOf('}', i + 1);
                var stop = end < 0 ? text.Length : end + 1;
                AppendNewlines(sb, text, i, stop);
                i = stop;
                continue;
            }

            sb.Append(c);
            i++;
        }
        return sb.ToString();

        static void AppendNewlines(System.Text.StringBuilder sb, string text, int from, int to)
        {
            for (int k = from; k < to; k++)
                if (text[k] == '\n') sb.Append('\n');
        }
    }

    // Splits off every ';'-terminated statement (outside strings) and leaves the remainder buffered.
    private static IEnumerable<string> TakeCompleteStatements(System.Text.StringBuilder buffer)
    {
        var text = buffer.ToString();
        var results = new List<string>();
        int start = 0;
        int idx;
        while ((idx = IndexOutsideStrings(text, ';', skipAssign: false, start)) >= 0)
        {
            var stmt = text[start..idx].Trim();
            if (stmt.Length > 0) results.Add(stmt);
            start = idx + 1;
        }
        buffer.Clear().Append(text[start..]);
        return results;
    }

    private static int IndexOutsideStrings(string text, char target, bool skipAssign, int start = 0)
    {
        char quote = '\0';
        for (int i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (c == '$') { i++; continue; }
                if (c == quote) quote = '\0';
                continue;
            }
            if (c == '\'' || c == '"') { quote = c; continue; }
            if (c == target)
            {
                if (skipAssign && i + 1 < text.Length && text[i + 1] == '=')
                    continue;
                return i;
            }
        }
        return -1;
    }

    private IEnumerable<ProgramInfo> ParsePousDirectly(IEnumerable<XElement> pous)
    {
        var programs = new List<ProgramInfo>();

        foreach (var pou in pous)
        {
            var program = new ProgramInfo
            {
                Name = pou.Attribute("Name")?.Value ?? "Unknown",
                Type = DeterminePouType(pou),
                Language = DeterminePouLanguage(pou),
                LocalTags = ParsePouVariables(pou),
                Properties = new Dictionary<string, string>
                {
                    ["Id"] = pou.Attribute("Id")?.Value ?? "",
                    ["SpecialFunc"] = pou.Attribute("SpecialFunc")?.Value ?? ""
                }
            };

            programs.Add(program);
        }

        return programs;
    }

    private IReadOnlyList<TagInfo> ParseGlobalVariables(XElement root)
    {
        var globalTags = new List<TagInfo>();

        // Find GVLs in the project
        var gvlElements = root.Descendants("GVL")
            .Concat(root.Descendants("GlobalVariableList"));

        foreach (var gvlElement in gvlElements)
        {
            var gvlName = gvlElement.Attribute("Name")?.Value ?? "Global";
            var variables = ParseGvlVariables(gvlElement);

            foreach (var variable in variables)
            {
                // Update scope to include GVL name
                globalTags.Add(variable with { Scope = gvlName });
            }
        }

        return globalTags;
    }

    private IReadOnlyList<IoModuleInfo> ParseIoConfiguration(XElement root)
    {
        var modules = new List<IoModuleInfo>();

        // Find I/O devices
        var deviceElements = root.Descendants("Device")
            .Concat(root.Descendants("Box"))
            .Concat(root.Descendants("Term"))
            .Concat(root.Descendants("Module"));

        foreach (var deviceElement in deviceElements)
        {
            var deviceType = deviceElement.Attribute("Type")?.Value ??
                           deviceElement.Attribute("DeviceType")?.Value;

            if (!string.IsNullOrEmpty(deviceType) && IsIoDevice(deviceType))
            {
                var module = new IoModuleInfo
                {
                    Name = deviceElement.Attribute("Name")?.Value ?? deviceType,
                    Type = DetermineIoModuleType(deviceType),
                    CatalogNumber = deviceType,
                    Vendor = "Beckhoff",
                    Location = deviceElement.Attribute("Id")?.Value,
                    Properties = new Dictionary<string, string>()
                };

                // Extract revision
                var revision = deviceElement.Attribute("Revision")?.Value;
                if (!string.IsNullOrEmpty(revision))
                {
                    module.Properties["Revision"] = revision;
                }

                // Count channels
                var channels = deviceElement.Descendants("Channel").Count();
                if (channels > 0)
                {
                    module.ChannelCount = channels;
                }

                // Extract configuration
                var configElements = deviceElement.Elements("Parameter");
                foreach (var config in configElements)
                {
                    var name = config.Attribute("Name")?.Value;
                    var value = config.Attribute("Value")?.Value ?? config.Value;
                    if (!string.IsNullOrEmpty(name))
                    {
                        module.Configuration[name] = value;
                    }
                }

                modules.Add(module);
            }
        }

        // Parse EtherCAT configuration
        var etherCatElements = root.Descendants("EtherCAT");
        foreach (var etherCat in etherCatElements)
        {
            var slaves = etherCat.Descendants("Slave");
            foreach (var slave in slaves)
            {
                var module = new IoModuleInfo
                {
                    Name = slave.Attribute("Name")?.Value ?? "EtherCAT Slave",
                    Type = "EtherCAT Device",
                    CatalogNumber = slave.Attribute("ProductCode")?.Value ?? "",
                    Vendor = slave.Attribute("Vendor")?.Value ?? "Unknown",
                    Location = $"Position {slave.Attribute("PhysAddr")?.Value ?? "?"}",
                    Properties = new Dictionary<string, string>
                    {
                        ["RevisionNo"] = slave.Attribute("RevisionNo")?.Value ?? ""
                    }
                };

                modules.Add(module);
            }
        }

        return modules;
    }

    private bool IsIoDevice(string deviceType)
    {
        var ioKeywords = new[]
        {
            "EL", "KL", "EP", "EK", "ES", "EJ",  // EtherCAT/K-bus terminals
            "Input", "Output", "I/O", "IO",
            "DI", "DO", "AI", "AO",
            "Encoder", "Drive", "Servo", "Motion"
        };

        return ioKeywords.Any(kw => deviceType.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }

    private string DetermineIoModuleType(string deviceType)
    {
        var upper = deviceType.ToUpper();

        // Beckhoff terminal types
        if (upper.StartsWith("EL1") || upper.StartsWith("KL1") || upper.StartsWith("EP1"))
            return "Digital Input";
        if (upper.StartsWith("EL2") || upper.StartsWith("KL2") || upper.StartsWith("EP2"))
            return "Digital Output";
        if (upper.StartsWith("EL3") || upper.StartsWith("KL3") || upper.StartsWith("EP3"))
            return "Analog Input";
        if (upper.StartsWith("EL4") || upper.StartsWith("KL4") || upper.StartsWith("EP4"))
            return "Analog Output";
        if (upper.StartsWith("EL5") || upper.StartsWith("KL5"))
            return "Position Measurement";
        if (upper.StartsWith("EL6") || upper.StartsWith("KL6") || upper.StartsWith("EP6"))
            return "Communication";
        if (upper.StartsWith("EL7") || upper.StartsWith("EP7"))
            return "Drive Technology";
        if (upper.StartsWith("EL9") || upper.StartsWith("KL9"))
            return "System Terminal";

        // Generic types
        if (upper.Contains("DIGITAL") && upper.Contains("INPUT")) return "Digital Input";
        if (upper.Contains("DIGITAL") && upper.Contains("OUTPUT")) return "Digital Output";
        if (upper.Contains("ANALOG") && upper.Contains("INPUT")) return "Analog Input";
        if (upper.Contains("ANALOG") && upper.Contains("OUTPUT")) return "Analog Output";
        if (upper.Contains("ENCODER")) return "Encoder";
        if (upper.Contains("DRIVE") || upper.Contains("SERVO")) return "Drive";
        if (upper.Contains("SAFETY")) return "Safety I/O";

        return "I/O Module";
    }
}