using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using PlcInfo.Models;

namespace PlcInfo.Parsing;

/// <summary>
/// Parser for Beckhoff TwinCAT 3 projects: the system manager project (.tsproj), a PLC project
/// (.plcproj) or a single PLC object file (.TcPOU, .TcGVL, .TcDUT, .TcIO, .TcTTO).
/// </summary>
/// <remarks>
/// A .tsproj references its PLC projects via Plc/Project@PrjFilePath, or via an .xti file under
/// _Config/PLC/ when "Independent Project File" is enabled. A .plcproj (MSBuild) lists its sources
/// as Compile@Include. Each source is a TcPlcObject XML file whose POU kind is the first keyword of
/// its Declaration text (see specs: beckhoff-twincat.md §2-§4).
/// </remarks>
public partial class TwinCatParser : IPlcProjectParser
{
    private const string MsBuildNamespace = "http://schemas.microsoft.com/developer/msbuild/2003";
    private const long MaxFileBytes = 64L * 1024 * 1024;

    private static readonly HashSet<string> ProjectExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tsproj", ".plcproj", ".tcpou", ".tcgvl", ".tcdut", ".tcio", ".tctto", ".tpy", ".tszip", ".tnzip", ".tpzip"
    };

    // PLC object files that carry logic, data types, variables or task assignments.
    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tcpou", ".tcgvl", ".tcdut", ".tcio", ".tctto"
    };

    private static readonly Regex ChannelName = new(@"\bChannel (\d+)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <inheritdoc/>
    public bool CanParse(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (ProjectExtensions.Contains(extension))
            return true;

        if (!extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) || !File.Exists(filePath))
            return false;

        try
        {
            var firstLines = File.ReadLines(filePath).Take(5).ToList();
            return firstLines.Any(line =>
                line.Contains("TcSmProject", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("TcPlcObject", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public Task<PlcProject> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (extension is ".tszip" or ".tnzip" or ".tpzip" || IsZipFile(filePath))
            throw new NotSupportedException(
                $"{Path.GetFileName(filePath)} is a TwinCAT archive. Extract it and pass the .tsproj or .plcproj file inside.");

        XDocument doc;
        try
        {
            doc = LoadXml(filePath);
        }
        catch (XmlException ex)
        {
            throw new InvalidOperationException($"{Path.GetFileName(filePath)} is not a valid TwinCAT XML file: {ex.Message}", ex);
        }

        var root = doc.Root ?? throw new InvalidOperationException("Invalid TwinCAT project file: no root element");
        cancellationToken.ThrowIfCancellationRequested();

        var project = root.Name.LocalName switch
        {
            "TcSmProject" => ParseSystemProject(root, filePath, cancellationToken),
            "Project" when root.Name.NamespaceName == MsBuildNamespace => ParsePlcProjectRoot(root, filePath, cancellationToken),
            "TcPlcObject" => ParseObjectFileRoot(root, filePath),
            "TcSmItem" => throw new NotSupportedException(
                $"{Path.GetFileName(filePath)} is part of a TwinCAT project (.xti). Pass the .tsproj that references it."),
            "PlcProjectInfo" => throw new NotSupportedException(
                $"{Path.GetFileName(filePath)} is a TwinCAT 2 compatible symbol file (.tpy), which is not supported. Pass the .tsproj or .plcproj."),
            _ => throw new NotSupportedException(
                $"{Path.GetFileName(filePath)} has root element <{root.Name.LocalName}>, which is not a TwinCAT 3 project. " +
                "Pass a .tsproj, .plcproj, .TcPOU, .TcGVL or .TcDUT file.")
        };

        return Task.FromResult(project);
    }

    // ---- Entry points per file kind ----

    private PlcProject ParseSystemProject(XElement root, string filePath, CancellationToken cancellationToken)
    {
        var projectDir = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".";
        var context = new ParseContext(projectDir);
        var projectElement = root.Element("Project");

        var tasks = ParseSystemTasks(projectElement);
        var plcNames = new List<string>();

        var plcProjects = projectElement?.Element("Plc")?.Elements("Project").ToList() ?? new List<XElement>();
        foreach (var plcProject in plcProjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (element, baseDir) = ResolveXtiReference(plcProject, "PLC", context);
            if (element == null)
                continue;

            var name = element.Attribute("Name")?.Value;
            var prjFilePath = element.Attribute("PrjFilePath")?.Value;
            if (string.IsNullOrEmpty(prjFilePath))
            {
                context.Warn($"PLC project '{name ?? "?"}' has no PrjFilePath, so its sources were not read.");
                continue;
            }

            var plcPath = ResolveProjectReference(projectDir, baseDir, prjFilePath);
            if (plcPath == null)
            {
                context.Warn($"PLC project file '{prjFilePath}' was skipped because it points outside the project directory.");
                continue;
            }
            if (!File.Exists(plcPath))
            {
                context.Warn($"PLC project file '{prjFilePath}' was not found.");
                continue;
            }

            var plcName = LoadPlcProject(plcPath, context, cancellationToken) ?? name ?? Path.GetFileNameWithoutExtension(plcPath);
            plcNames.Add(plcName);
        }

        if (plcProjects.Count == 0)
            context.Warn("The project references no PLC project, so only tasks and I/O were read.");

        var modules = ParseIoTree(projectElement, context);
        var linkCount = ApplyIoMappings(root, modules, context);

        tasks = MergeTasks(tasks, context.Tasks);
        AssignProgramsToTasks(tasks, context.Programs);

        var settings = projectElement?.Element("System")?.Element("Settings");
        var controller = new ControllerInfo
        {
            Name = plcNames.FirstOrDefault() ?? Path.GetFileNameWithoutExtension(filePath),
            Vendor = "Beckhoff",
            Type = "TwinCAT 3",
            FirmwareRevision = root.Attribute("TcVersion")?.Value ?? "",
            Properties = new Dictionary<string, string>()
        };
        AddIfPresent(controller.Properties, "AmsNetId", projectElement?.Attribute("TargetNetId")?.Value);
        AddIfPresent(controller.Properties, "Target64Bit", projectElement?.Attribute("Target64Bit")?.Value);
        AddIfPresent(controller.Properties, "MaxCpus", settings?.Attribute("MaxCpus")?.Value);

        var metadata = new Dictionary<string, string>
        {
            ["TwinCATVersion"] = root.Attribute("TcVersion")?.Value ?? "",
            ["TcSmVersion"] = root.Attribute("TcSmVersion")?.Value ?? "",
            ["ProjectGuid"] = projectElement?.Attribute("ProjectGUID")?.Value ?? "",
            ["PlcProjects"] = string.Join(", ", plcNames)
        };
        if (linkCount > 0)
            metadata["IoLinks"] = linkCount.ToString(CultureInfo.InvariantCulture);

        return BuildProject(filePath, controller, tasks, modules, context, metadata);
    }

    private PlcProject ParsePlcProjectRoot(XElement root, string filePath, CancellationToken cancellationToken)
    {
        var projectDir = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".";
        var context = new ParseContext(projectDir);
        var name = LoadPlcProject(Path.GetFullPath(filePath), context, cancellationToken, root)
                   ?? Path.GetFileNameWithoutExtension(filePath);

        var tasks = MergeTasks(new List<TaskInfo>(), context.Tasks);
        AssignProgramsToTasks(tasks, context.Programs);

        var properties = root.Elements(XName.Get("PropertyGroup", MsBuildNamespace)).Elements().ToList();
        var controller = new ControllerInfo
        {
            Name = name,
            Vendor = "Beckhoff",
            Type = "TwinCAT 3 PLC project",
            FirmwareRevision = properties.FirstOrDefault(e => e.Name.LocalName == "ProgramVersion")?.Value ?? ""
        };

        var metadata = new Dictionary<string, string>
        {
            ["ProjectGuid"] = properties.FirstOrDefault(e => e.Name.LocalName == "ProjectGuid")?.Value ?? "",
            ["PlcProjects"] = name
        };

        return BuildProject(filePath, controller, tasks, Array.Empty<IoModuleInfo>(), context, metadata);
    }

    private PlcProject ParseObjectFileRoot(XElement root, string filePath)
    {
        var projectDir = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".";
        var context = new ParseContext(projectDir);
        ReadPlcObjects(root, Path.GetFileName(filePath), plcProject: null, context);

        var tasks = MergeTasks(new List<TaskInfo>(), context.Tasks);
        AssignProgramsToTasks(tasks, context.Programs);

        var metadata = new Dictionary<string, string>
        {
            ["ProductVersion"] = root.Attribute("ProductVersion")?.Value ?? "",
            ["TcPlcObjectVersion"] = root.Attribute("Version")?.Value ?? ""
        };
        return BuildProject(filePath, controller: null, tasks, Array.Empty<IoModuleInfo>(), context, metadata);
    }

    private static PlcProject BuildProject(
        string filePath,
        ControllerInfo? controller,
        IReadOnlyList<TaskInfo> tasks,
        IReadOnlyList<IoModuleInfo> modules,
        ParseContext context,
        Dictionary<string, string> metadata)
    {
        if (context.Libraries.Count > 0)
            metadata["Libraries"] = string.Join(", ", context.Libraries.Distinct(StringComparer.OrdinalIgnoreCase));
        if (context.Warnings.Count > 0)
            metadata["Warnings"] = string.Join("\n", context.Warnings);

        return new PlcProject
        {
            Vendor = "Beckhoff",
            Format = "TwinCAT",
            FilePath = filePath,
            Controller = controller,
            Tasks = tasks,
            Programs = context.Programs,
            Tags = context.Tags,
            IoModules = modules,
            DataTypes = context.DataTypes,
            LastModified = File.GetLastWriteTime(filePath),
            Metadata = metadata
        };
    }

    // ---- PLC project (.plcproj) ----

    /// <summary>Loads every PLC source a .plcproj lists. Returns the PLC project name, or null if unreadable.</summary>
    private string? LoadPlcProject(string plcPath, ParseContext context, CancellationToken cancellationToken, XElement? root = null)
    {
        var displayPath = context.Relative(plcPath);
        if (root == null)
        {
            try
            {
                root = LoadXml(plcPath).Root;
            }
            catch (Exception ex) when (ex is XmlException or IOException or InvalidDataException)
            {
                context.Warn($"PLC project file '{displayPath}' could not be read: {ex.Message}");
                return null;
            }
        }

        XNamespace ns = MsBuildNamespace;
        if (root == null || root.Name != ns + "Project")
        {
            context.Warn($"'{displayPath}' is not an MSBuild PLC project, so its sources were not read.");
            return null;
        }

        var name = root.Elements(ns + "PropertyGroup").Elements(ns + "Name").FirstOrDefault()?.Value
                   ?? Path.GetFileNameWithoutExtension(plcPath);
        var plcDir = Path.GetDirectoryName(plcPath) ?? context.RootDir;

        var sources = root.Descendants(ns + "Compile")
            .Select(c => c.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrEmpty(include))
            .Select(include => UnescapeMsBuild(include!))
            .Where(include => SourceExtensions.Contains(Path.GetExtension(include)))
            .ToList();

        if (sources.Count == 0)
            context.Warn($"PLC project '{name}' lists no POU, GVL, DUT or task files.");

        foreach (var include in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = ResolveProjectReference(context.RootDir, plcDir, include);
            if (sourcePath == null)
            {
                context.Warn($"PLC project '{name}': source '{include}' was skipped because it points outside the project directory.");
                continue;
            }
            if (!File.Exists(sourcePath))
            {
                context.Warn($"PLC project '{name}': source file '{include}' was not found.");
                continue;
            }

            XElement? objectRoot;
            try
            {
                objectRoot = LoadXml(sourcePath).Root;
            }
            catch (Exception ex) when (ex is XmlException or IOException or InvalidDataException)
            {
                context.Warn($"PLC project '{name}': source file '{include}' could not be read: {ex.Message}");
                continue;
            }

            if (objectRoot?.Name.LocalName != "TcPlcObject")
            {
                context.Warn($"PLC project '{name}': source file '{include}' is not a TcPlcObject file and was skipped.");
                continue;
            }
            ReadPlcObjects(objectRoot, context.Relative(sourcePath), name, context);
        }

        foreach (var reference in root.Descendants(ns + "PlaceholderReference").Concat(root.Descendants(ns + "LibraryReference")))
        {
            var include = reference.Attribute("Include")?.Value;
            if (!string.IsNullOrEmpty(include))
                context.Libraries.Add(include);
        }

        return name;
    }

    // MSBuild escapes special characters in item specs as %XX (e.g. %3B for ';').
    private static string UnescapeMsBuild(string value)
    {
        if (!value.Contains('%'))
            return value;
        return Regex.Replace(value, "%([0-9A-Fa-f]{2})",
            m => ((char)int.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());
    }

    // ---- PLC object files (TcPlcObject) ----

    private static void ReadPlcObjects(XElement root, string displayPath, string? plcProject, ParseContext context)
    {
        var recognised = 0;
        foreach (var element in root.Elements())
        {
            switch (element.Name.LocalName)
            {
                case "POU":
                case "Itf":
                    context.Programs.Add(ReadPou(element, displayPath, plcProject, context));
                    recognised++;
                    break;
                case "GVL":
                    ReadGvl(element, plcProject, context);
                    recognised++;
                    break;
                case "DUT":
                    ReadDut(element, displayPath, plcProject, context);
                    recognised++;
                    break;
                case "Task":
                    context.Tasks.Add(ReadPlcTask(element, plcProject));
                    recognised++;
                    break;
            }
        }

        if (recognised == 0)
            context.Warn($"'{displayPath}' contains no POU, interface, GVL, DUT or task.");
    }

    private static ProgramInfo ReadPou(XElement element, string displayPath, string? plcProject, ParseContext context)
    {
        var declaration = element.Element("Declaration")?.Value ?? "";
        var header = ParsePouHeader(declaration);
        var name = element.Attribute("Name")?.Value ?? header.Name ?? "Unknown";
        var isInterface = element.Name.LocalName == "Itf";

        var type = MapPouKind(header.Keyword);
        if (type == null)
        {
            type = isInterface ? "Interface" : "Unknown";
            if (!isInterface)
                context.Warn($"POU '{name}' in '{displayPath}' does not start with PROGRAM, FUNCTION_BLOCK, FUNCTION or INTERFACE.");
        }

        var routines = new List<RoutineInfo>();
        var implementation = element.Element("Implementation");
        if (implementation != null)
            routines.Add(BuildRoutine(name, "Main", name, implementation, null, null));

        foreach (var child in ChildObjects(element))
        {
            var childName = child.Attribute("Name")?.Value ?? child.Name.LocalName;
            var childDeclaration = child.Element("Declaration")?.Value;
            if (child.Name.LocalName != "Property")
            {
                routines.Add(BuildRoutine(childName, child.Name.LocalName, name, child.Element("Implementation"), childDeclaration, child));
                continue;
            }

            var accessors = child.Elements().Where(e => e.Name.LocalName is "Get" or "Set").ToList();
            if (accessors.Count == 0)
                routines.Add(BuildRoutine(childName, "Property", name, null, childDeclaration, child));
            foreach (var accessor in accessors)
            {
                var routine = BuildRoutine($"{childName}.{accessor.Name.LocalName}", "Property", name,
                    accessor.Element("Implementation"), childDeclaration, accessor);
                var accessorDeclaration = accessor.Element("Declaration")?.Value.Trim();
                if (!string.IsNullOrEmpty(accessorDeclaration))
                    routine.Properties["AccessorDeclaration"] = accessorDeclaration;
                routines.Add(routine);
            }
        }

        var program = new ProgramInfo
        {
            Name = name,
            Type = type,
            Language = routines.FirstOrDefault(r => r.Type == "Main")?.Language ?? "Declaration",
            Routines = routines,
            LocalTags = ParseDeclarationText(declaration, name),
            Properties = new Dictionary<string, string>()
        };

        AddIfPresent(program.Properties, "Id", element.Attribute("Id")?.Value);
        AddIfPresent(program.Properties, "SpecialFunc", element.Attribute("SpecialFunc")?.Value);
        AddIfPresent(program.Properties, "Extends", header.Extends);
        AddIfPresent(program.Properties, "ReturnType", header.ReturnType);
        if (header.Implements.Count > 0) program.Properties["Implements"] = string.Join(", ", header.Implements);
        if (header.Modifiers.Count > 0) program.Properties["Modifiers"] = string.Join(" ", header.Modifiers);
        AddIfPresent(program.Properties, "PlcProject", plcProject);
        program.Properties["File"] = displayPath;
        foreach (var tag in program.LocalTags)
            AddIfPresent(tag.Properties, "PlcProject", plcProject);

        return program;
    }

    // Methods, properties, actions and transitions, including any grouped under <Folder> elements.
    private static IEnumerable<XElement> ChildObjects(XElement pou)
    {
        foreach (var child in pou.Elements())
        {
            if (child.Name.LocalName == "Folder")
            {
                foreach (var nested in ChildObjects(child))
                    yield return nested;
            }
            else if (child.Name.LocalName is "Method" or "Property" or "Action" or "Transition")
            {
                yield return child;
            }
        }
    }

    private static RoutineInfo BuildRoutine(
        string name, string type, string owner, XElement? implementation, string? declaration, XElement? source)
    {
        var routine = new RoutineInfo
        {
            Name = name,
            Type = type,
            ProgramName = owner,
            Properties = new Dictionary<string, string>()
        };

        var body = implementation?.Elements().FirstOrDefault();
        if (body == null)
        {
            // Interface members and abstract methods have a declaration but no body.
            routine.Language = "Declaration";
        }
        else
        {
            routine.Language = body.Name.LocalName;
            if (body.Name.LocalName is "ST" or "IL")
            {
                routine.Source = body.Value;
            }
            else
            {
                // SFC, LD, FBD and CFC bodies are XML archives; rendering them is not supported yet.
                routine.Properties["Graphical"] = "true";
            }
        }

        AddIfPresent(routine.Properties, "Id", source?.Attribute("Id")?.Value);
        var trimmed = declaration?.Trim();
        if (!string.IsNullOrEmpty(trimmed))
            routine.Properties["Declaration"] = trimmed;
        return routine;
    }

    private static void ReadGvl(XElement element, string? plcProject, ParseContext context)
    {
        var name = element.Attribute("Name")?.Value ?? "GVL";
        foreach (var tag in ParseDeclarationText(element.Element("Declaration")?.Value ?? "", name))
        {
            AddIfPresent(tag.Properties, "PlcProject", plcProject);
            context.Tags.Add(tag);
        }
    }

    private static void ReadDut(XElement element, string displayPath, string? plcProject, ParseContext context)
    {
        var name = element.Attribute("Name")?.Value ?? "Unknown";
        var dataType = ParseDutDeclaration(element.Element("Declaration")?.Value ?? "");
        if (dataType == null)
        {
            context.Warn($"Data type '{name}' in '{displayPath}' has no TYPE ... END_TYPE declaration and was skipped.");
            return;
        }

        dataType.Name = name;
        AddIfPresent(dataType.Properties, "Id", element.Attribute("Id")?.Value);
        AddIfPresent(dataType.Properties, "PlcProject", plcProject);
        context.DataTypes.Add(dataType);
    }

    private static TaskInfo ReadPlcTask(XElement element, string? plcProject)
    {
        // .TcTTO CycleTime is in microseconds (the file's own comment says so).
        double? rate = long.TryParse(element.Element("CycleTime")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var us)
            ? us / 1000.0
            : null;

        var task = new TaskInfo
        {
            Name = element.Attribute("Name")?.Value ?? "PlcTask",
            Type = rate.HasValue ? "Cyclic" : "Freewheeling",
            Priority = int.TryParse(element.Element("Priority")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 20,
            Rate = rate,
            ProgramNames = element.Elements("PouCall")
                .Select(c => c.Element("Name")?.Value.Trim())
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .ToList(),
            Properties = new Dictionary<string, string>()
        };
        AddIfPresent(task.Properties, "PlcProject", plcProject);
        return task;
    }

    // ---- Tasks ----

    private static List<TaskInfo> ParseSystemTasks(XElement? projectElement)
    {
        var tasks = new List<TaskInfo>();
        foreach (var element in projectElement?.Element("System")?.Element("Tasks")?.Elements("Task") ?? Enumerable.Empty<XElement>())
        {
            // System/Tasks/Task@CycleTime is in 100 ns ticks (100000 = 10 ms), unlike .TcTTO (µs).
            double? rate = long.TryParse(element.Attribute("CycleTime")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks)
                ? ticks / 10000.0
                : null;

            var task = new TaskInfo
            {
                Name = element.Element("Name")?.Value ?? element.Attribute("Name")?.Value ?? "Task",
                Type = rate.HasValue ? "Cyclic" : "Freewheeling",
                Priority = int.TryParse(element.Attribute("Priority")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 0,
                Rate = rate,
                IsEnabled = !string.Equals(element.Attribute("Disabled")?.Value, "true", StringComparison.OrdinalIgnoreCase),
                Properties = new Dictionary<string, string>()
            };
            AddIfPresent(task.Properties, "Id", element.Attribute("Id")?.Value);
            AddIfPresent(task.Properties, "AmsPort", element.Attribute("AmsPort")?.Value);
            AddIfPresent(task.Properties, "Affinity", element.Attribute("Affinity")?.Value);
            tasks.Add(task);
        }
        return tasks;
    }

    // System tasks carry the runtime settings; the PLC's .TcTTO files say which programs each task calls.
    private static List<TaskInfo> MergeTasks(List<TaskInfo> systemTasks, List<TaskInfo> plcTasks)
    {
        var merged = new List<TaskInfo>(systemTasks);
        foreach (var plcTask in plcTasks)
        {
            var existing = merged.FirstOrDefault(t => string.Equals(t.Name, plcTask.Name, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                merged.Add(plcTask);
                continue;
            }

            existing.ProgramNames = existing.ProgramNames.Concat(plcTask.ProgramNames).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            existing.Rate ??= plcTask.Rate;
            foreach (var (key, value) in plcTask.Properties)
                existing.Properties.TryAdd(key, value);
        }
        return merged;
    }

    private static void AssignProgramsToTasks(IEnumerable<TaskInfo> tasks, IEnumerable<ProgramInfo> programs)
    {
        var byName = programs.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        foreach (var task in tasks)
        {
            foreach (var programName in task.ProgramNames)
            {
                if (!byName.TryGetValue(programName, out var matches))
                    continue;
                foreach (var program in matches)
                    program.TaskName ??= task.Name;
            }
        }
    }

    // ---- I/O ----

    private static List<IoModuleInfo> ParseIoTree(XElement? projectElement, ParseContext context)
    {
        var modules = new List<IoModuleInfo>();
        foreach (var deviceReference in projectElement?.Element("Io")?.Elements("Device") ?? Enumerable.Empty<XElement>())
        {
            var (device, _) = ResolveXtiReference(deviceReference, "IO", context);
            if (device == null)
                continue;

            var name = device.Element("Name")?.Value ?? device.Attribute("RemoteName")?.Value ?? $"Device {device.Attribute("Id")?.Value}";
            var isEtherCat = device.Element("EtherCAT") != null || device.Attribute("DevType")?.Value == "111";
            var module = new IoModuleInfo
            {
                Name = name,
                Type = isEtherCat ? "EtherCAT Master" : "I/O Device",
                Location = name,
                CommFormat = isEtherCat ? "EtherCAT" : null,
                Properties = new Dictionary<string, string>()
            };
            AddIfPresent(module.Properties, "Id", device.Attribute("Id")?.Value);
            AddIfPresent(module.Properties, "DevType", device.Attribute("DevType")?.Value);
            AddIfPresent(module.Properties, "AmsNetId", device.Attribute("AmsNetId")?.Value);
            modules.Add(module);

            foreach (var box in device.Elements("Box"))
                ParseBox(box, name, name, modules, context);
        }
        return modules;
    }

    // EtherCAT terminals are nested <Box> elements that follow the topology; there is no <Term> element.
    private static void ParseBox(XElement boxReference, string parentName, string parentPath, List<IoModuleInfo> modules, ParseContext context)
    {
        var (box, _) = ResolveXtiReference(boxReference, "IO", context);
        if (box == null)
            return;

        var name = box.Element("Name")?.Value ?? $"Box {box.Attribute("Id")?.Value}";
        var path = $"{parentPath}^{name}";
        var etherCat = box.Element("EtherCAT");

        var catalog = etherCat?.Attribute("Desc")?.Value;
        if (string.IsNullOrEmpty(catalog))
        {
            // Default display names look like "Term 2 (EL1004)".
            var open = name.LastIndexOf('(');
            if (open >= 0 && name.EndsWith(')'))
                catalog = name[(open + 1)..^1];
        }

        var channels = etherCat?.Elements("Pdo")
            .Select(p => ChannelName.Match(p.Attribute("Name")?.Value ?? ""))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .Count() ?? 0;

        var vendorId = etherCat?.Attribute("VendorId")?.Value;
        var module = new IoModuleInfo
        {
            Name = name,
            Type = string.IsNullOrEmpty(catalog) ? "I/O Module" : DetermineIoModuleType(catalog),
            CatalogNumber = catalog,
            Vendor = vendorId == null ? null : IsBeckhoffVendor(vendorId) ? "Beckhoff" : $"VendorId {vendorId}",
            Location = path,
            ChannelCount = channels,
            CommFormat = etherCat != null ? "EtherCAT" : null,
            ParentModule = parentName,
            Properties = new Dictionary<string, string>()
        };
        AddIfPresent(module.Properties, "Id", box.Attribute("Id")?.Value);
        AddIfPresent(module.Properties, "BoxType", box.Attribute("BoxType")?.Value);
        AddIfPresent(module.Properties, "Description", etherCat?.Attribute("Type")?.Value);
        AddIfPresent(module.Properties, "VendorId", vendorId);
        AddIfPresent(module.Properties, "ProductCode", etherCat?.Attribute("ProductCode")?.Value);
        AddIfPresent(module.Properties, "RevisionNo", etherCat?.Attribute("RevisionNo")?.Value);
        AddIfPresent(module.Properties, "EtherCatAddr", etherCat?.Attribute("EtherCatAddr")?.Value);
        modules.Add(module);

        foreach (var child in box.Elements("Box"))
            ParseBox(child, name, path, modules, context);
    }

    private static bool IsBeckhoffVendor(string vendorId) =>
        vendorId.StartsWith("#x", StringComparison.OrdinalIgnoreCase) &&
        long.TryParse(vendorId[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id) && id == 2;

    /// <summary>
    /// Applies Mappings/OwnerA/OwnerB/Link: links between a PLC instance ("TIPC^...") and an I/O
    /// item ("TIID^..."). The linked PLC symbol is added to the module's AssociatedTags and the
    /// declaring tag is marked as an I/O tag. Returns the number of PLC-to-I/O links.
    /// </summary>
    private static int ApplyIoMappings(XElement root, List<IoModuleInfo> modules, ParseContext context)
    {
        var byPath = modules.Where(m => m.Location != null).GroupBy(m => m.Location!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var associated = new Dictionary<IoModuleInfo, List<string>>(ReferenceEqualityComparer.Instance);
        foreach (var m in modules)
            associated[m] = new List<string>();
        var count = 0;

        foreach (var ownerA in root.Element("Mappings")?.Elements("OwnerA") ?? Enumerable.Empty<XElement>())
        {
            var nameA = ownerA.Attribute("Name")?.Value ?? "";
            foreach (var ownerB in ownerA.Elements("OwnerB"))
            {
                var nameB = ownerB.Attribute("Name")?.Value ?? "";
                bool plcIsA;
                if (nameA.StartsWith("TIPC^", StringComparison.Ordinal) && nameB.StartsWith("TIID^", StringComparison.Ordinal))
                    plcIsA = true;
                else if (nameA.StartsWith("TIID^", StringComparison.Ordinal) && nameB.StartsWith("TIPC^", StringComparison.Ordinal))
                    plcIsA = false;
                else
                    continue;

                var ioOwner = (plcIsA ? nameB : nameA)["TIID^".Length..];
                var plcOwner = (plcIsA ? nameA : nameB)["TIPC^".Length..];
                var plcProject = plcOwner.Split('^')[0];
                var module = FindModule(byPath, ioOwner);

                foreach (var link in ownerB.Elements("Link"))
                {
                    var plcVar = (plcIsA ? link.Attribute("VarA") : link.Attribute("VarB"))?.Value;
                    if (string.IsNullOrEmpty(plcVar))
                        continue;

                    // "PlcTask Inputs^MAIN.stRack.ST_In^Input1": the first segment is the process image.
                    var segments = plcVar.Split('^');
                    var symbol = string.Join(".", segments.Length > 1 ? segments[1..] : segments);
                    count++;
                    if (module == null)
                        continue;

                    associated[module].Add(symbol);
                    MarkLinkedTag(symbol, plcProject, module.Name, context);
                }
            }
        }

        foreach (var (module, tags) in associated)
            if (tags.Count > 0)
                module.AssociatedTags = tags.Distinct(StringComparer.Ordinal).ToList();
        return count;
    }

    private static IoModuleInfo? FindModule(Dictionary<string, IoModuleInfo> byPath, string ioPath)
    {
        var path = ioPath;
        while (true)
        {
            if (byPath.TryGetValue(path, out var module))
                return module;
            var cut = path.LastIndexOf('^');
            if (cut <= 0)
                return null;
            path = path[..cut];
        }
    }

    private static void MarkLinkedTag(string symbol, string plcProject, string moduleName, ParseContext context)
    {
        // "GVL_IO.arrIds[2]" → scope GVL_IO, tag arrIds; "MAIN.fb.x" → POU MAIN, local fb.
        var parts = symbol.Split('.');
        if (parts.Length < 2)
            return;
        var scope = parts[0];
        var tagName = parts[1].Split('[')[0];

        bool Matches(TagInfo t) =>
            string.Equals(t.Scope, scope, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(t.Name, tagName, StringComparison.OrdinalIgnoreCase) &&
            (!t.Properties.TryGetValue("PlcProject", out var p) || string.Equals(p, plcProject, StringComparison.OrdinalIgnoreCase));

        foreach (var tag in context.Tags.Where(Matches).Concat(context.Programs.SelectMany(p => p.LocalTags).Where(Matches)))
        {
            tag.IsIoTag = true;
            tag.IoModule ??= moduleName;
        }
    }

    private static string DetermineIoModuleType(string catalog)
    {
        var upper = catalog.ToUpperInvariant();
        bool Starts(params string[] prefixes) => prefixes.Any(p => upper.StartsWith(p, StringComparison.Ordinal));

        if (Starts("EK", "BK")) return "Coupler";
        if (Starts("EL1", "KL1", "EP1", "ES1")) return "Digital Input";
        if (Starts("EL2", "KL2", "EP2", "ES2")) return "Digital Output";
        if (Starts("EL3", "KL3", "EP3", "ES3")) return "Analog Input";
        if (Starts("EL4", "KL4", "EP4", "ES4")) return "Analog Output";
        if (Starts("EL5", "KL5")) return "Position Measurement";
        if (Starts("EL6", "KL6", "EP6")) return "Communication";
        if (Starts("EL7", "EP7")) return "Drive Technology";
        if (Starts("EL9", "KL9")) return "System Terminal";
        return "I/O Module";
    }

    // ---- Shared helpers ----

    /// <summary>
    /// Follows an "Independent Project File" reference such as &lt;Project File="X.xti"/&gt;.
    /// The .xti lives under _Config/&lt;section&gt;/ (e.g. _Config/PLC/TcUnit.xti in tcunit/TcUnit).
    /// Returns the element to read and the directory its relative paths are based on.
    /// </summary>
    private static (XElement? Element, string BaseDir) ResolveXtiReference(XElement element, string section, ParseContext context)
    {
        var file = element.Attribute("File")?.Value;
        if (string.IsNullOrEmpty(file))
            return (element, context.RootDir);

        var configDir = Path.Combine(context.RootDir, "_Config");
        var candidate = ResolveProjectReference(context.RootDir, Path.Combine(configDir, section), file);
        if (candidate != null && !File.Exists(candidate) && Directory.Exists(configDir) && Path.GetFileName(file) == file)
        {
            // Nested items (e.g. boxes) are stored in subfolders named after their parent.
            candidate = Directory.EnumerateFiles(configDir, file, SearchOption.AllDirectories).FirstOrDefault() ?? candidate;
        }

        if (candidate == null)
        {
            context.Warn($"Project file '{file}' was skipped because it points outside the project directory.");
            return (null, context.RootDir);
        }
        if (!File.Exists(candidate))
        {
            context.Warn($"Project file '{file}' was not found under _Config/{section}.");
            return (null, context.RootDir);
        }

        try
        {
            var inner = LoadXml(candidate).Root?.Element(element.Name);
            if (inner == null)
            {
                context.Warn($"Project file '{context.Relative(candidate)}' has no <{element.Name.LocalName}> element.");
                return (null, context.RootDir);
            }
            return (inner, Path.GetDirectoryName(candidate) ?? context.RootDir);
        }
        catch (Exception ex) when (ex is XmlException or IOException or InvalidDataException)
        {
            context.Warn($"Project file '{context.Relative(candidate)}' could not be read: {ex.Message}");
            return (null, context.RootDir);
        }
    }

    /// <summary>Loads XML without DTD processing or external resolution, and with a size limit.</summary>
    private static XDocument LoadXml(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxFileBytes)
            throw new InvalidDataException($"The file is larger than {MaxFileBytes / (1024 * 1024)} MB.");

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };
        using var stream = File.OpenRead(path);
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader);
    }

    private static bool IsZipFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[4];
            return stream.Read(header) == 4 && header[0] == 'P' && header[1] == 'K' && header[2] == 3 && header[3] == 4;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static void AddIfPresent(Dictionary<string, string> properties, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            properties[key] = value;
    }

    /// <summary>Accumulates everything read from a project and its referenced files.</summary>
    private sealed class ParseContext
    {
        public ParseContext(string rootDir) => RootDir = Path.GetFullPath(rootDir);

        /// <summary>No referenced file may resolve outside this directory.</summary>
        public string RootDir { get; }
        public List<ProgramInfo> Programs { get; } = new();
        public List<TagInfo> Tags { get; } = new();
        public List<DataTypeInfo> DataTypes { get; } = new();
        public List<TaskInfo> Tasks { get; } = new();
        public List<string> Libraries { get; } = new();
        public List<string> Warnings { get; } = new();

        public void Warn(string message) => Warnings.Add(message);

        public string Relative(string path) => Path.GetRelativePath(RootDir, path);
    }
}
