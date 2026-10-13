using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using PlcInfo.Models;
using PlcInfo.Parsing.PlcOpen;

namespace PlcInfo.Parsing;

/// <summary>
/// Parser for PLCopen TC6 XML exports (<c>http://www.plcopen.org/xml/tc6_0200</c> and <c>tc6_0201</c>),
/// as written by CODESYS ("Export PLCopenXML"), CODESYS-based tools (ctrlX, TwinCAT PLC Control),
/// Beremiz and OpenPLC Editor. CODESYS extensions in <c>addData</c>
/// (<c>http://www.3s-software.com/plcopenxml/...</c>) are read for methods, properties,
/// interfaces, inheritance, global variable lists, task settings and the device tree.
/// </summary>
/// <remarks>
/// Binary CODESYS <c>.project</c> and <c>.library</c> files are recognised but rejected with a
/// <see cref="NotSupportedException"/> that explains how to export PLCopen XML.
/// </remarks>
public class CodesysParser : IPlcProjectParser
{
    private const string CodesysAddData = "http://www.3s-software.com/plcopenxml/";
    private static readonly Regex PlcOpenNamespace = new(@"^http://www\.plcopen\.org/xml/tc6_02\d\d$", RegexOptions.CultureInvariant);

    /// <inheritdoc/>
    public bool CanParse(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        // Claimed so the user gets a clear message instead of "no parser found".
        if (extension is ".project" or ".library" or ".export")
            return true;

        return extension == ".xml" && File.Exists(filePath) && IsPlcOpenDocument(filePath);
    }

    /// <summary>
    /// True when the file's root element is a PLCopen TC6 v2.x <c>project</c>.
    /// </summary>
    internal static bool IsPlcOpenDocument(string filePath)
    {
        try
        {
            using var reader = XmlReader.Create(filePath, SafeReaderSettings());
            return reader.MoveToContent() == XmlNodeType.Element &&
                   reader.LocalName == "project" &&
                   PlcOpenNamespace.IsMatch(reader.NamespaceURI);
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<PlcProject> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (extension is ".project" or ".library" && !LooksLikeXml(filePath))
        {
            throw new NotSupportedException(
                $"{Path.GetFileName(filePath)} is a binary CODESYS {extension} file, which plcinfo cannot read. " +
                "Open it in CODESYS, choose Project > Export PLCopenXML..., select the objects to export, " +
                "and run plcinfo on the exported .xml file.");
        }

        XDocument document;
        try
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            using var reader = XmlReader.Create(stream, SafeReaderSettings(async: true));
            document = await XDocument.LoadAsync(reader, LoadOptions.PreserveWhitespace, cancellationToken);
        }
        catch (XmlException ex)
        {
            throw new InvalidOperationException($"{Path.GetFileName(filePath)} is not well-formed XML: {ex.Message}", ex);
        }

        var root = document.Root ?? throw new InvalidOperationException($"{Path.GetFileName(filePath)} has no root element.");
        if (root.Name.LocalName != "project" || !PlcOpenNamespace.IsMatch(root.Name.NamespaceName))
        {
            throw new NotSupportedException(
                $"{Path.GetFileName(filePath)} is not a PLCopen XML (TC6 v2.0/v2.01) export: its root element is " +
                $"'{root.Name.LocalName}' in namespace '{root.Name.NamespaceName}'. " +
                "In CODESYS, use Project > Export PLCopenXML... to create a supported file.");
        }

        var schema = root.Name.NamespaceName[(root.Name.NamespaceName.LastIndexOf('/') + 1)..];
        StripNamespaces(root);

        return new Reader(root, filePath, schema).Read();
    }

    private static bool LooksLikeXml(string filePath)
    {
        var buffer = new byte[256];
        int read;
        using (var stream = File.OpenRead(filePath))
            read = stream.Read(buffer, 0, buffer.Length);

        var i = 0;
        if (read >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
            i = 3;
        while (i < read && buffer[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            i++;
        return i < read && buffer[i] == (byte)'<';
    }

    private static XmlReaderSettings SafeReaderSettings(bool async = false) => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        Async = async,
        IgnoreProcessingInstructions = true
    };

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

    /// <summary>
    /// Reads one namespace-stripped PLCopen document.
    /// </summary>
    private sealed class Reader(XElement root, string filePath, string schema)
    {
        private readonly List<string> _warnings = new();

        public PlcProject Read()
        {
            var fileHeader = root.Element("fileHeader");
            var contentHeader = root.Element("contentHeader");
            var productName = fileHeader?.Attribute("productName")?.Value ?? string.Empty;
            var companyName = fileHeader?.Attribute("companyName")?.Value ?? string.Empty;
            var hasCodesysData = root.Descendants("data")
                .Any(d => d.Attribute("name")?.Value.StartsWith(CodesysAddData, StringComparison.Ordinal) == true);
            var vendor = productName.Contains("CODESYS", StringComparison.OrdinalIgnoreCase)
                ? "CODESYS"
                : Meaningful(companyName) ?? Meaningful(productName) ?? (hasCodesysData ? "CODESYS" : "PLCopen");

            var configurations = root.Element("instances")?.Element("configurations")?.Elements("configuration").ToList()
                                 ?? new List<XElement>();

            var programs = ReadPous();
            var tasks = ReadTasks(configurations);
            foreach (var task in tasks)
            {
                foreach (var name in task.ProgramNames)
                {
                    var program = programs.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (program != null)
                        program.TaskName ??= task.Name;
                }
            }

            var metadata = new Dictionary<string, string>
            {
                ["Schema"] = schema,
                ["ProductName"] = productName,
                ["ProductVersion"] = fileHeader?.Attribute("productVersion")?.Value ?? string.Empty,
                ["CompanyName"] = companyName,
                ["CreationDateTime"] = fileHeader?.Attribute("creationDateTime")?.Value ?? string.Empty
            };
            AddIfPresent(metadata, "ModificationDateTime", contentHeader?.Attribute("modificationDateTime")?.Value);
            AddIfPresent(metadata, "ProjectVersion", contentHeader?.Attribute("version")?.Value);
            AddIfPresent(metadata, "Author", contentHeader?.Attribute("author")?.Value);
            AddIfPresent(metadata, "Organization", contentHeader?.Attribute("organization")?.Value);
            AddIfPresent(metadata, "Description", PlcOpenText.Documentation(contentHeader?.Element("Comment")));
            if (configurations.Count > 0)
                metadata["Configurations"] = string.Join(", ", configurations.Select(c => c.Attribute("name")?.Value));
            var libraries = root.Descendants("Library").Select(l => l.Attribute("Name")?.Value)
                .Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
            if (libraries.Count > 0)
                metadata["Libraries"] = string.Join(", ", libraries);

            var project = new PlcProject
            {
                Vendor = vendor,
                Format = $"PLCopen XML ({schema})",
                FilePath = filePath,
                Controller = ReadController(contentHeader, configurations, vendor, productName),
                Tasks = tasks,
                Programs = programs,
                Tags = ReadGlobalVariables(configurations),
                IoModules = ReadDevices(configurations),
                DataTypes = ReadDataTypes(),
                LastModified = File.GetLastWriteTime(filePath),
                Metadata = metadata
            };

            if (_warnings.Count > 0)
                metadata["Warnings"] = string.Join("\n", _warnings.Distinct());
            return project;
        }

        private static string? Meaningful(string? value) =>
            string.IsNullOrWhiteSpace(value) || value is "Unknown" or "Unnamed" or "Blank" ? null : value.Trim();

        private static void AddIfPresent(Dictionary<string, string> properties, string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                properties[key] = value;
        }

        /// <summary>
        /// CODESYS addData blocks of one kind (e.g. "pou", "method") below <paramref name="scope"/>:
        /// anywhere when <paramref name="deep"/>, else only the element's own addData.
        /// </summary>
        private static IEnumerable<XElement> AddData(XElement scope, string kind, bool deep = true)
        {
            var name = CodesysAddData + kind;
            var data = deep
                ? scope.Descendants("data")
                : scope.Elements("addData").Elements("data");
            return data.Where(d => d.Attribute("name")?.Value == name);
        }

        // ---------------------------------------------------------------- controller and devices

        private ControllerInfo ReadController(XElement? contentHeader, List<XElement> configurations, string vendor, string productName)
        {
            var controller = new ControllerInfo
            {
                Name = contentHeader?.Attribute("name")?.Value.NullIfEmpty() ?? Path.GetFileNameWithoutExtension(filePath),
                Vendor = vendor,
                Type = productName
            };

            var first = configurations.FirstOrDefault();
            if (first != null)
            {
                controller.Type = first.Attribute("name")?.Value ?? controller.Type;
                var identification = Identification(DeviceOf(first));
                if (identification != null)
                {
                    controller.FirmwareRevision = identification.Element("Version")?.Value ?? string.Empty;
                    AddIfPresent(controller.Properties, "DeviceType", identification.Element("Type")?.Value);
                    AddIfPresent(controller.Properties, "DeviceId", identification.Element("Id")?.Value);
                }
                AddIfPresent(controller.Properties, "Applications",
                    string.Join(", ", first.Elements("resource").Select(r => r.Attribute("name")?.Value)));
            }

            var information = contentHeader?.Descendants("ProjectInformation").FirstOrDefault();
            foreach (var property in information?.Elements("property") ?? Enumerable.Empty<XElement>())
                AddIfPresent(controller.Properties, "Project" + property.Attribute("name")?.Value, property.Value);

            return controller;
        }

        private static XElement? DeviceOf(XElement configuration) =>
            configuration.Elements("addData").Elements("data")
                .FirstOrDefault(d => d.Attribute("name")?.Value == "Device")?.Element("Device");

        private static XElement? Identification(XElement? device) =>
            device?.Element("DeviceType")?.Elements()
                .FirstOrDefault(e => e.Name.LocalName is "DeviceIdentification" or "ModuleIdentification");

        /// <summary>
        /// CODESYS writes the device tree below each top-level configuration as nested
        /// <c>addData/data[@name='configurations']/configurations/configuration</c> elements, each with
        /// its own <c>Device</c>. Every nested device becomes a module; its channels are the
        /// parameters marked <c>Attributes@channel</c> on that device only, so a coupler and the
        /// modules below it are not counted twice.
        /// </summary>
        private static IReadOnlyList<IoModuleInfo> ReadDevices(List<XElement> configurations)
        {
            var modules = new List<IoModuleInfo>();
            var stack = new Stack<(XElement Configuration, string Parent, int Depth)>();
            foreach (var top in Enumerable.Reverse(configurations))
            {
                foreach (var child in NestedConfigurations(top).Reverse())
                    stack.Push((child, top.Attribute("name")?.Value ?? string.Empty, 1));
            }

            while (stack.Count > 0)
            {
                var (configuration, parent, depth) = stack.Pop();
                var name = configuration.Attribute("name")?.Value ?? "?";
                var device = DeviceOf(configuration);
                var identification = Identification(device);

                var channels = device?.Descendants("Parameter")
                    .Where(p => p.Element("Attributes")?.Attribute("channel") != null)
                    .ToList() ?? new List<XElement>();
                var inputs = channels.Where(c => Direction(c).Contains("input", StringComparison.OrdinalIgnoreCase)).ToList();
                var outputs = channels.Where(c => Direction(c).Contains("output", StringComparison.OrdinalIgnoreCase)).ToList();

                var module = new IoModuleInfo
                {
                    Name = name,
                    Type = (inputs.Count > 0, outputs.Count > 0) switch
                    {
                        (true, true) => "Input/Output",
                        (true, false) => "Input",
                        (false, true) => "Output",
                        _ => "Device"
                    },
                    CatalogNumber = identification?.Element("ModuleId")?.Value
                                    ?? (identification == null ? null : $"{identification.Element("Type")?.Value}/{identification.Element("Id")?.Value}"),
                    ParentModule = parent.NullIfEmpty(),
                    ChannelCount = ChannelCount(channels),
                    AssociatedTags = device?.Descendants("Mapping").Elements("Element")
                        .Select(e => e.Value.Trim()).Where(v => v.Length > 0).ToList() ?? new List<string>()
                };
                AddIfPresent(module.Properties, "Version", identification?.Element("Version")?.Value);
                if (inputs.Count > 0)
                    module.Properties["InputChannels"] = ChannelCount(inputs).ToString(CultureInfo.InvariantCulture);
                if (outputs.Count > 0)
                    module.Properties["OutputChannels"] = ChannelCount(outputs).ToString(CultureInfo.InvariantCulture);
                modules.Add(module);

                if (depth < 64)
                {
                    foreach (var child in NestedConfigurations(configuration).Reverse())
                        stack.Push((child, name, depth + 1));
                }
            }
            return modules;
        }

        private static string Direction(XElement parameter) =>
            parameter.Element("Attributes")?.Attribute("channel")?.Value ?? string.Empty;

        // A channel parameter of a bit-field type lists its bits as Value/Element.
        private static int ChannelCount(IEnumerable<XElement> parameters) =>
            parameters.Sum(p => Math.Max(1, p.Element("Value")?.Elements("Element").Count() ?? 0));

        private static IEnumerable<XElement> NestedConfigurations(XElement configuration) =>
            configuration.Elements("addData").Elements("data")
                .Where(d => d.Attribute("name")?.Value == "configurations")
                .Elements("configurations").Elements("configuration");

        // ---------------------------------------------------------------- tasks

        private List<TaskInfo> ReadTasks(List<XElement> configurations)
        {
            var tasks = new List<TaskInfo>();
            foreach (var configuration in configurations)
            {
                foreach (var resource in configuration.Elements("resource"))
                {
                    foreach (var task in resource.Elements("task"))
                        tasks.Add(ReadTask(task, configuration, resource));
                }
            }
            return tasks;
        }

        private TaskInfo ReadTask(XElement task, XElement configuration, XElement resource)
        {
            var name = task.Attribute("name")?.Value ?? "?";
            var interval = task.Attribute("interval")?.Value.NullIfEmpty();
            var single = task.Attribute("single")?.Value.NullIfEmpty();
            var settings = AddData(task, "tasksettings", deep: false).Elements("TaskSettings").FirstOrDefault();
            var kind = settings?.Attribute("KindOfTask")?.Value.NullIfEmpty();

            var info = new TaskInfo
            {
                Name = name,
                Priority = int.TryParse(task.Attribute("priority")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var priority) ? priority : 0,
                // A pouInstance nested in a task runs in that task (TC6 §instances). CODESYS leaves typeName
                // empty and puts the program name in name; Beremiz names the instance and puts the program in typeName.
                ProgramNames = task.Elements("pouInstance")
                    .Select(p => p.Attribute("typeName")?.Value.NullIfEmpty() ?? p.Attribute("name")?.Value)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Select(n => n!)
                    .ToList()
            };

            var rate = PlcOpenText.ParseDurationMs(interval);
            if (interval != null && rate == null && !IsIdentifier(interval))
                _warnings.Add($"Task '{name}' has an interval '{interval}' that is not a duration, so its rate is unknown.");
            // CODESYS keeps an Interval in TaskSettings for every kind of task; it is only a rate for cyclic tasks.
            info.Rate = rate ?? (string.Equals(kind, "Cyclic", StringComparison.OrdinalIgnoreCase)
                ? PlcOpenText.ToMilliseconds(settings?.Attribute("Interval")?.Value, settings?.Attribute("IntervalUnit")?.Value)
                : null);

            info.Type = kind?.ToLowerInvariant() switch
            {
                "cyclic" => "Periodic",
                "freewheeling" => "Freewheeling",
                "event" or "externalevent" => "Event",
                "status" => "Status",
                { } other => other,
                null when single != null => "Event",
                null when interval != null => "Periodic",
                _ => "Freewheeling"
            };

            var watchdog = settings?.Element("Watchdog");
            if (watchdog != null && string.Equals(watchdog.Attribute("Enabled")?.Value, "true", StringComparison.OrdinalIgnoreCase))
                info.WatchdogTimeout = PlcOpenText.ToMilliseconds(watchdog.Attribute("Time")?.Value, watchdog.Attribute("TimeUnit")?.Value);

            AddIfPresent(info.Properties, "Interval", interval);
            AddIfPresent(info.Properties, "Single", single);
            AddIfPresent(info.Properties, "KindOfTask", kind);
            AddIfPresent(info.Properties, "ExternalEvent", settings?.Attribute("ExternalEvent")?.Value);
            AddIfPresent(info.Properties, "Configuration", configuration.Attribute("name")?.Value);
            AddIfPresent(info.Properties, "Resource", resource.Attribute("name")?.Value);
            var instances = task.Elements("pouInstance")
                .Where(p => !string.IsNullOrEmpty(p.Attribute("typeName")?.Value))
                .Select(p => $"{p.Attribute("name")?.Value}: {p.Attribute("typeName")?.Value}")
                .ToList();
            if (instances.Count > 0)
                info.Properties["Instances"] = string.Join(", ", instances);
            return info;
        }

        private static bool IsIdentifier(string text) =>
            Regex.IsMatch(text, @"^[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        // ---------------------------------------------------------------- POUs

        private List<ProgramInfo> ReadPous()
        {
            var programs = new List<ProgramInfo>();

            foreach (var pou in root.Element("types")?.Element("pous")?.Elements("pou") ?? Enumerable.Empty<XElement>())
                programs.Add(ReadPou(pou, null));

            // CODESYS full-project exports put the application's POUs in the resource's addData.
            foreach (var data in AddData(root, "pou"))
            {
                var resource = data.Ancestors("resource").FirstOrDefault();
                foreach (var pou in data.Elements("pou"))
                    programs.Add(ReadPou(pou, resource));
            }

            foreach (var data in AddData(root, "interface"))
            {
                foreach (var definition in data.Elements("Interface"))
                    programs.Add(ReadInterface(definition));
            }

            return programs;
        }

        private ProgramInfo ReadPou(XElement pou, XElement? resource)
        {
            var name = pou.Attribute("name")?.Value ?? "?";
            var pouType = pou.Attribute("pouType")?.Value;
            var program = new ProgramInfo
            {
                Name = name,
                Type = pouType switch
                {
                    "program" => "Program",
                    "functionBlock" => "FunctionBlock",
                    "function" => "Function",
                    null or "" => "Program",
                    _ => pouType
                },
                Description = PlcOpenText.Documentation(pou.Element("documentation"))
                              ?? PlcOpenText.Documentation(pou.Element("interface")?.Element("documentation")),
                LocalTags = ReadInterfaceVariables(pou.Element("interface"), name)
            };
            if (pouType is not (null or "" or "program" or "functionBlock" or "function"))
                _warnings.Add($"POU '{name}' has an unknown pouType '{pouType}'.");

            var routines = new List<RoutineInfo>();
            foreach (var body in pou.Elements("body"))
                routines.Add(ReadBody(body, body.Attribute("WorksheetName")?.Value.NullIfEmpty() ?? name, "Main", name, $"POU '{name}'"));
            program.Language = routines.FirstOrDefault()?.Language ?? string.Empty;

            foreach (var action in pou.Element("actions")?.Elements("action") ?? Enumerable.Empty<XElement>())
            {
                var actionName = action.Attribute("name")?.Value ?? "?";
                if (action.Element("body") is { } body)
                    routines.Add(ReadBody(body, actionName, "Action", name, $"Action '{name}.{actionName}'"));
            }

            foreach (var transition in pou.Element("transitions")?.Elements("transition") ?? Enumerable.Empty<XElement>())
            {
                var transitionName = transition.Attribute("name")?.Value ?? "?";
                if (transition.Element("body") is { } body)
                    routines.Add(ReadBody(body, transitionName, "Transition", name, $"Transition '{name}.{transitionName}'"));
            }

            // CODESYS object-oriented extensions live in the POU's own addData.
            foreach (var method in AddData(pou, "method", deep: false).Elements("Method"))
                routines.Add(ReadMethod(method, name));
            foreach (var property in AddData(pou, "property", deep: false).Elements("Property"))
                routines.AddRange(ReadProperty(property, name));

            program.Routines = routines;

            ReadPouProperties(pou, program.Properties);
            if (resource != null)
            {
                AddIfPresent(program.Properties, "Resource", resource.Attribute("name")?.Value);
                AddIfPresent(program.Properties, "Configuration", resource.Parent?.Attribute("name")?.Value);
            }
            return program;
        }

        private static void ReadPouProperties(XElement pou, Dictionary<string, string> properties)
        {
            var @interface = pou.Element("interface");
            AddIfPresent(properties, "ReturnType", PlcOpenText.FormatType(@interface?.Element("returnType")));

            if (@interface != null)
            {
                AddInheritance(AddData(@interface, "pouinheritance", deep: false).Elements("Inheritance").FirstOrDefault(), properties);
                AddIfPresent(properties, "Attributes", Attributes(@interface));
                AddIfPresent(properties, "AccessModifier", AccessModifier(@interface));
            }

            AddIfPresent(properties, "Declaration",
                PlcOpenText.FormattedText(AddData(pou, "interfaceasplaintext", deep: false).Elements("InterfaceAsPlainText").FirstOrDefault())?.Trim());
        }

        private static void AddInheritance(XElement? inheritance, Dictionary<string, string> properties)
        {
            if (inheritance == null)
                return;
            var extends = inheritance.Elements("Extends").Select(e => e.Value.Trim()).Where(v => v.Length > 0).ToList();
            var implements = inheritance.Elements("Implements").Select(e => e.Value.Trim()).Where(v => v.Length > 0).ToList();
            if (extends.Count > 0)
                properties["Extends"] = string.Join(", ", extends);
            if (implements.Count > 0)
                properties["Implements"] = string.Join(", ", implements);
        }

        private static string? Attributes(XElement scope)
        {
            var attributes = AddData(scope, "attributes", deep: false).Elements("Attributes").Elements("Attribute")
                .Select(a =>
                {
                    var value = a.Attribute("Value")?.Value;
                    return string.IsNullOrEmpty(value) ? a.Attribute("Name")?.Value : $"{a.Attribute("Name")?.Value}={value}";
                })
                .Where(a => !string.IsNullOrEmpty(a))
                .ToList();
            return attributes.Count > 0 ? string.Join("; ", attributes) : null;
        }

        private static string? AccessModifier(XElement scope) =>
            AddData(scope, "accessmodifiers", deep: false).Elements("AccessModifiers").FirstOrDefault()
                ?.Attributes().Where(a => string.Equals(a.Value, "true", StringComparison.OrdinalIgnoreCase))
                .Select(a => a.Name.LocalName.ToUpperInvariant()).FirstOrDefault();

        private RoutineInfo ReadMethod(XElement method, string owner)
        {
            var name = method.Attribute("name")?.Value ?? "?";
            var routine = method.Element("body") is { } body
                ? ReadBody(body, name, "Method", owner, $"Method '{owner}.{name}'")
                : new RoutineInfo { Name = name, Type = "Method", ProgramName = owner };

            var @interface = method.Element("interface");
            routine.Description = PlcOpenText.Documentation(@interface?.Element("documentation"))
                                  ?? PlcOpenText.Documentation(method.Element("documentation"));
            AddSignature(@interface, routine.Properties);
            if (@interface != null)
                AddIfPresent(routine.Properties, "Attributes", Attributes(@interface));
            return routine;
        }

        private IEnumerable<RoutineInfo> ReadProperty(XElement property, string owner)
        {
            var name = property.Attribute("name")?.Value ?? "?";
            var type = PlcOpenText.FormatType(property.Element("interface")?.Element("returnType"));
            var description = PlcOpenText.Documentation(property.Element("interface")?.Element("documentation"));
            var routines = new List<RoutineInfo>();
            foreach (var (element, accessor) in new[] { ("GetAccessor", "Get"), ("SetAccessor", "Set") })
            {
                if (property.Element(element) is not { } accessorElement)
                    continue;
                var routine = accessorElement.Element("body") is { } body
                    ? ReadBody(body, $"{name}.{accessor}", "Property", owner, $"Property '{owner}.{name}.{accessor}'")
                    : new RoutineInfo { Name = $"{name}.{accessor}", Type = "Property", ProgramName = owner };
                routine.Description = description;
                routine.Properties["Accessor"] = accessor;
                AddIfPresent(routine.Properties, "DataType", type);
                AddIfPresent(routine.Properties, "AccessModifier",
                    AccessModifier(accessorElement.Element("interface") ?? accessorElement) ?? AccessModifier(property.Element("interface") ?? property));
                routines.Add(routine);
            }

            if (routines.Count == 0)
            {
                var routine = new RoutineInfo { Name = name, Type = "Property", ProgramName = owner, Description = description };
                AddIfPresent(routine.Properties, "DataType", type);
                routines.Add(routine);
            }
            return routines;
        }

        private static void AddSignature(XElement? @interface, Dictionary<string, string> properties)
        {
            if (@interface == null)
                return;
            AddIfPresent(properties, "ReturnType", PlcOpenText.FormatType(@interface.Element("returnType")));
            AddIfPresent(properties, "AccessModifier", AccessModifier(@interface));
            var parameters = @interface.Elements()
                .Where(s => s.Name.LocalName is "inputVars" or "outputVars" or "inOutVars")
                .SelectMany(s => s.Elements("variable").Select(v =>
                    $"{SectionKeyword(s.Name.LocalName)} {v.Attribute("name")?.Value} : {PlcOpenText.FormatType(v.Element("type"))}"))
                .ToList();
            if (parameters.Count > 0)
                properties["Parameters"] = string.Join("; ", parameters);
        }

        private static string SectionKeyword(string section) => section switch
        {
            "inputVars" => "VAR_INPUT",
            "outputVars" => "VAR_OUTPUT",
            _ => "VAR_IN_OUT"
        };

        /// <summary>
        /// A CODESYS interface (addData kind "interface"): method and property prototypes without bodies.
        /// </summary>
        private ProgramInfo ReadInterface(XElement definition)
        {
            var name = definition.Attribute("name")?.Value ?? "?";
            var routines = new List<RoutineInfo>();
            foreach (var method in definition.Element("Methods")?.Elements("Method") ?? Enumerable.Empty<XElement>())
                routines.Add(ReadMethod(method, name));
            foreach (var property in definition.Element("Properties")?.Elements("Property") ?? Enumerable.Empty<XElement>())
            {
                var routine = new RoutineInfo
                {
                    Name = property.Attribute("name")?.Value ?? "?",
                    Type = "Property",
                    ProgramName = name,
                    Description = PlcOpenText.Documentation(property.Element("interface")?.Element("documentation"))
                };
                AddIfPresent(routine.Properties, "DataType", PlcOpenText.FormatType(property.Element("interface")?.Element("returnType")));
                var accessors = new[] { "Get", "Set" }.Where(a => property.Element(a + "Accessor") != null).ToList();
                if (accessors.Count > 0)
                    routine.Properties["Accessors"] = string.Join(", ", accessors);
                routines.Add(routine);
            }

            var program = new ProgramInfo
            {
                Name = name,
                Type = "Interface",
                Description = PlcOpenText.Documentation(definition.Element("documentation")),
                Routines = routines
            };
            AddInheritance(definition.Element("Inheritance"), program.Properties);
            return program;
        }

        private RoutineInfo ReadBody(XElement body, string name, string type, string owner, string context)
        {
            var routine = new RoutineInfo
            {
                Name = name,
                Type = type,
                ProgramName = owner,
                Description = PlcOpenText.Documentation(body.Element("documentation"))
            };

            // CODESYS CFC: an empty ST placeholder plus the real diagram in addData (seen in CODESYS V3.5 SP18).
            var cfc = AddData(body, "cfc", deep: false).Elements("CFC").FirstOrDefault();
            var content = cfc ?? body.Elements().FirstOrDefault(e => e.Name.LocalName is "ST" or "IL" or "LD" or "FBD" or "SFC");
            if (content == null)
            {
                var other = body.Elements().FirstOrDefault(e => e.Name.LocalName is not ("addData" or "documentation"))?.Name.LocalName;
                if (other != null)
                    _warnings.Add($"{context} has a body in an unsupported language ({other}).");
                routine.Language = other ?? string.Empty;
                return routine;
            }

            routine.Language = content.Name.LocalName;
            switch (routine.Language)
            {
                case "ST":
                case "IL":
                    routine.Source = PlcOpenText.FormattedText(content);
                    break;
                case "SFC":
                    var summary = PlcOpenSfc.Summarize(content, context, _warnings);
                    routine.Networks = new[] { summary };
                    routine.NetworkCount = 1;
                    foreach (var (key, value) in summary.Properties)
                        routine.Properties[key] = value;
                    break;
                default:
                    routine.Networks = new PlcOpenGraph(content, context, _warnings).BuildNetworks(routine.Language);
                    routine.NetworkCount = routine.Networks.Count;
                    break;
            }
            return routine;
        }

        // ---------------------------------------------------------------- variables

        private static readonly Dictionary<string, string> SectionUsage = new()
        {
            ["inputVars"] = "Input",
            ["outputVars"] = "Output",
            ["inOutVars"] = "InOut",
            ["localVars"] = "Local",
            ["tempVars"] = "Temp",
            ["externalVars"] = "External",
            ["globalVars"] = "Global",
            ["accessVars"] = "Access"
        };

        private static List<TagInfo> ReadInterfaceVariables(XElement? @interface, string scope)
        {
            var tags = new List<TagInfo>();
            if (@interface == null)
                return tags;
            foreach (var section in @interface.Elements())
            {
                if (SectionUsage.TryGetValue(section.Name.LocalName, out var usage))
                    tags.AddRange(ReadVariables(section, scope, usage, null));
            }
            return tags;
        }

        /// <summary>
        /// Reads the variables of a varList. Qualifiers (constant, retain, persistent) belong to the
        /// list, not the variable (TC6 §varList); <paramref name="qualifiers"/> overrides them per variable.
        /// </summary>
        private static IEnumerable<TagInfo> ReadVariables(XElement section, string scope, string usage, Dictionary<string, XElement>? qualifiers)
        {
            foreach (var variable in section.Elements("variable"))
            {
                var name = variable.Attribute("name")?.Value ?? "?";
                var type = variable.Element("type");
                var address = variable.Attribute("address")?.Value.NullIfEmpty();
                var tag = new TagInfo
                {
                    Name = name,
                    DataType = PlcOpenText.FormatType(type),
                    Scope = scope,
                    Usage = usage,
                    Description = PlcOpenText.Documentation(variable.Element("documentation")),
                    InitialValue = PlcOpenText.FormatValue(variable.Element("initialValue")),
                    Address = address,
                    IsIoTag = address != null && (address.StartsWith("%I", StringComparison.OrdinalIgnoreCase) ||
                                                  address.StartsWith("%Q", StringComparison.OrdinalIgnoreCase)),
                    ArrayDimensions = PlcOpenText.ArrayElementCounts(type)
                };

                var list = qualifiers != null && qualifiers.TryGetValue(name, out var specific) ? specific : section;
                foreach (var qualifier in new[] { "constant", "retain", "nonretain", "persistent", "nonpersistent" })
                {
                    if (string.Equals(list.Attribute(qualifier)?.Value, "true", StringComparison.OrdinalIgnoreCase))
                        tag.Properties[char.ToUpperInvariant(qualifier[0]) + qualifier[1..]] = "true";
                }
                AddIfPresent(tag.Properties, "Attributes", Attributes(variable));
                yield return tag;
            }
        }

        private List<TagInfo> ReadGlobalVariables(List<XElement> configurations)
        {
            var tags = new List<TagInfo>();
            foreach (var configuration in configurations)
            {
                foreach (var list in configuration.Elements("globalVars"))
                    tags.AddRange(ReadGlobalList(list, configuration, null));
                foreach (var resource in configuration.Elements("resource"))
                {
                    foreach (var list in resource.Elements("globalVars"))
                        tags.AddRange(ReadGlobalList(list, configuration, resource));
                }
            }

            // CODESYS library exports keep GVLs in the project's addData (seen in CODESYS V3.5 SP4).
            foreach (var data in AddData(root, "globalvars"))
            {
                var resource = data.Ancestors("resource").FirstOrDefault();
                foreach (var list in data.Elements("globalVars"))
                    tags.AddRange(ReadGlobalList(list, resource?.Parent, resource));
            }
            return tags;
        }

        private static IEnumerable<TagInfo> ReadGlobalList(XElement list, XElement? configuration, XElement? resource)
        {
            // CODESYS flattens a GVL with several sections (VAR_GLOBAL and VAR_GLOBAL CONSTANT) into one
            // list and records the real sections in addData "mixedattrsvarlist" (seen in CODESYS V3.5 SP21).
            Dictionary<string, XElement>? qualifiers = null;
            foreach (var section in AddData(list, "mixedattrsvarlist", deep: false).Elements("MixedAttrsVarList").Elements("globalVars"))
            {
                qualifiers ??= new Dictionary<string, XElement>(StringComparer.Ordinal);
                foreach (var variable in section.Elements("variable"))
                {
                    if (variable.Attribute("name")?.Value is { } name)
                        qualifiers.TryAdd(name, section);
                }
            }

            var scope = list.Attribute("name")?.Value.NullIfEmpty() ?? "Global";
            foreach (var tag in ReadVariables(list, scope, "Global", qualifiers))
            {
                AddIfPresent(tag.Properties, "Configuration", configuration?.Attribute("name")?.Value);
                AddIfPresent(tag.Properties, "Resource", resource?.Attribute("name")?.Value);
                yield return tag;
            }
        }

        // ---------------------------------------------------------------- data types

        private List<DataTypeInfo> ReadDataTypes()
        {
            var types = new List<DataTypeInfo>();
            foreach (var dataType in root.Element("types")?.Element("dataTypes")?.Elements("dataType") ?? Enumerable.Empty<XElement>())
                types.Add(ReadDataType(dataType));

            // CODESYS full-project exports put the application's DUTs in the resource's addData.
            foreach (var data in AddData(root, "datatype"))
            {
                foreach (var dataType in data.Elements("dataType"))
                    types.Add(ReadDataType(dataType));
            }

            // TC6 2.01 has no union; CODESYS exports one as addData "union".
            foreach (var data in AddData(root, "union"))
            {
                foreach (var union in data.Elements("union"))
                    types.Add(ReadComposite(union.Attribute("name")?.Value ?? "?", "Union", union));
            }
            return types;
        }

        private DataTypeInfo ReadDataType(XElement dataType)
        {
            var name = dataType.Attribute("name")?.Value ?? "?";
            var baseType = dataType.Element("baseType");
            var definition = baseType?.Elements().FirstOrDefault(e => e.Name.LocalName is not ("addData" or "documentation"));

            DataTypeInfo info;
            switch (definition?.Name.LocalName)
            {
                case "struct":
                    info = ReadComposite(name, "Struct", definition);
                    break;
                case "union":
                    info = ReadComposite(name, "Union", definition);
                    break;
                case "enum":
                    var documentation = AddData(dataType, "enumvaluedocumentation", deep: false)
                        .Elements("EnumValueDocumentation").Elements("EnumValue")
                        .Where(e => e.Element("Name") != null)
                        .GroupBy(e => e.Element("Name")!.Value)
                        .ToDictionary(g => g.Key, g => PlcOpenText.Documentation(g.First().Element("Documentation")));
                    info = new DataTypeInfo
                    {
                        Name = name,
                        Kind = "Enum",
                        BaseType = PlcOpenText.FormatType(definition.Element("baseType")).NullIfEmpty(),
                        Members = definition.Element("values")?.Elements("value").Select(v => new DataTypeMember
                        {
                            Name = v.Attribute("name")?.Value ?? "?",
                            InitialValue = v.Attribute("value")?.Value,
                            Description = documentation.GetValueOrDefault(v.Attribute("name")?.Value ?? string.Empty)
                        }).ToList() ?? new List<DataTypeMember>()
                    };
                    break;
                case null:
                    _warnings.Add($"Data type '{name}' has no base type.");
                    info = new DataTypeInfo { Name = name, Kind = "Alias" };
                    break;
                default:
                    // Arrays, pointers, subranges, strings and renames are aliases of the type they spell out.
                    info = new DataTypeInfo { Name = name, Kind = "Alias", BaseType = PlcOpenText.FormatType(baseType) };
                    info.Properties["Construct"] = definition.Name.LocalName switch
                    {
                        "array" => "Array",
                        "pointer" => "Pointer",
                        "reference" => "Reference",
                        "subrangeSigned" or "subrangeUnsigned" => "Subrange",
                        "derived" => "Derived",
                        _ => "Elementary"
                    };
                    if (definition.Name.LocalName == "array")
                        info.Properties["Dimensions"] = string.Join(", ", PlcOpenText.ArrayBounds(definition));
                    break;
            }

            info.Description = PlcOpenText.Documentation(dataType.Element("documentation"));
            AddIfPresent(info.Properties, "InitialValue", PlcOpenText.FormatValue(dataType.Element("initialValue")));
            AddInheritance(AddData(dataType, "datatypeinheritance", deep: false).Elements("Inheritance").FirstOrDefault(), info.Properties);
            AddIfPresent(info.Properties, "Attributes", Attributes(dataType));
            return info;
        }

        private static DataTypeInfo ReadComposite(string name, string kind, XElement members) => new()
        {
            Name = name,
            Kind = kind,
            Description = PlcOpenText.Documentation(members.Element("documentation")),
            Members = members.Elements("variable").Select(v =>
            {
                var type = v.Element("type");
                var array = PlcOpenText.ArrayOf(type);
                return new DataTypeMember
                {
                    Name = v.Attribute("name")?.Value ?? "?",
                    DataType = PlcOpenText.FormatType(type),
                    Dimensions = array == null ? null : string.Join(", ", PlcOpenText.ArrayBounds(array)),
                    Description = PlcOpenText.Documentation(v.Element("documentation")),
                    InitialValue = PlcOpenText.FormatValue(v.Element("initialValue"))
                };
            }).ToList()
        };
    }
}
