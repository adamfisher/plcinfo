using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PlcInfo.Models;

namespace PlcInfo.Parsing;

/// <summary>
/// Parser for Siemens TIA Portal project archives (.ap14-.ap19).
/// </summary>
public class SiemensTiaPortalParser : IPlcProjectParser
{
    private static readonly string[] SupportedExtensions = { ".ap14", ".ap15", ".ap15_1", ".ap16", ".ap17", ".ap18", ".ap19", ".zap13", ".zap14", ".zap15", ".zap16", ".zap17", ".zap18", ".zap19" };

    /// <inheritdoc/>
    public bool CanParse(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return SupportedExtensions.Any(ext => ext.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc/>
    public async Task<PlcProject> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        var tempPath = Path.Combine(Path.GetTempPath(), $"tia_{Guid.NewGuid()}");

        try
        {
            // Extract archive
            ZipFile.ExtractToDirectory(filePath, tempPath);

            var version = ExtractTiaVersion(filePath);
            var projectData = await ParseProjectStructure(tempPath, cancellationToken);

            return new PlcProject
            {
                Vendor = "Siemens",
                Format = version,
                FilePath = filePath,
                Controller = projectData.Controller,
                Blocks = projectData.Blocks,
                Programs = projectData.Programs,
                Tags = projectData.Tags,
                IoModules = projectData.Modules,
                LastModified = File.GetLastWriteTime(filePath),
                Metadata = new Dictionary<string, string>
                {
                    ["TiaVersion"] = version,
                    ["ProjectLanguage"] = projectData.Language
                }
            };
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                try
                {
                    Directory.Delete(tempPath, true);
                }
                catch { }
            }
        }
    }

    private string ExtractTiaVersion(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLower();
        return extension switch
        {
            ".zap13" => "TIA Portal V13",
            ".ap14" or ".zap14" => "TIA Portal V14",
            ".ap15" or ".zap15" => "TIA Portal V15",
            ".ap15_1" => "TIA Portal V15.1",
            ".ap16" or ".zap16" => "TIA Portal V16",
            ".ap17" or ".zap17" => "TIA Portal V17",
            ".ap18" or ".zap18" => "TIA Portal V18",
            ".ap19" or ".zap19" => "TIA Portal V19",
            _ => "TIA Portal Unknown"
        };
    }

    private async Task<ProjectData> ParseProjectStructure(string extractPath, CancellationToken cancellationToken)
    {
        var projectData = new ProjectData();

        // Look for project files
        var projectFiles = Directory.GetFiles(extractPath, "*.xml", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f).Contains("Project", StringComparison.OrdinalIgnoreCase) ||
                       Path.GetFileName(f).Contains("Device", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Parse main project file
        var mainProjectFile = projectFiles.FirstOrDefault(f =>
            Path.GetFileName(f).Equals("Project.xml", StringComparison.OrdinalIgnoreCase));

        if (mainProjectFile != null)
        {
            await ParseMainProject(mainProjectFile, projectData, cancellationToken);
        }

        // Parse device configurations
        var deviceFiles = Directory.GetFiles(extractPath, "*Device*.xml", SearchOption.AllDirectories);
        foreach (var deviceFile in deviceFiles)
        {
            await ParseDeviceConfiguration(deviceFile, projectData, cancellationToken);
        }

        // Parse PLC programs
        var plcPath = Path.Combine(extractPath, "PlcBlocks");
        if (Directory.Exists(plcPath))
        {
            await ParsePlcBlocks(plcPath, projectData, cancellationToken);
        }

        // Alternative path for PLC data
        var softwarePath = Directory.GetDirectories(extractPath, "*Software*", SearchOption.AllDirectories).FirstOrDefault();
        if (softwarePath != null)
        {
            await ParseSoftwareBlocks(softwarePath, projectData, cancellationToken);
        }

        // Parse tags
        var tagTablePath = Directory.GetDirectories(extractPath, "*TagTable*", SearchOption.AllDirectories).FirstOrDefault();
        if (tagTablePath != null)
        {
            await ParseTagTables(tagTablePath, projectData, cancellationToken);
        }

        // Parse hardware
        var hwPath = Directory.GetDirectories(extractPath, "*Hardware*", SearchOption.AllDirectories).FirstOrDefault();
        if (hwPath != null)
        {
            ParseHardwareConfiguration(hwPath, projectData);
        }

        return projectData;
    }

    private async Task ParseMainProject(string filePath, ProjectData projectData, CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            var doc = XDocument.Parse(content);

            var projectElement = doc.Root;
            if (projectElement != null)
            {
                projectData.Language = projectElement.Attribute("Language")?.Value ?? "en-US";

                var nameElement = projectElement.Element("Name");
                if (nameElement != null)
                {
                    projectData.ProjectName = nameElement.Value;
                }
            }
        }
        catch { }
    }

    private async Task ParseDeviceConfiguration(string filePath, ProjectData projectData, CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            var doc = XDocument.Parse(content);

            var deviceElement = doc.Root;
            if (deviceElement != null)
            {
                var controller = new ControllerInfo
                {
                    Name = deviceElement.Element("Name")?.Value ?? Path.GetFileNameWithoutExtension(filePath),
                    Type = deviceElement.Element("TypeName")?.Value ?? "S7-1500",
                    Vendor = "Siemens",
                    CpuType = deviceElement.Element("DeviceType")?.Value,
                    FirmwareRevision = deviceElement.Element("FirmwareVersion")?.Value ?? "",
                    Properties = new Dictionary<string, string>()
                };

                // Extract additional properties
                var properties = deviceElement.Elements("Property");
                foreach (var prop in properties)
                {
                    var name = prop.Attribute("Name")?.Value;
                    var value = prop.Value;
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
                    {
                        controller.Properties[name] = value;
                    }
                }

                projectData.Controller = controller;
            }
        }
        catch { }
    }

    private async Task ParsePlcBlocks(string plcPath, ProjectData projectData, CancellationToken cancellationToken)
    {
        var blockFiles = Directory.GetFiles(plcPath, "*.xml", SearchOption.AllDirectories);

        foreach (var blockFile in blockFiles)
        {
            var block = await ParseBlockFile(blockFile, cancellationToken);
            if (block != null)
            {
                projectData.Blocks.Add(block);
            }
        }
    }

    private async Task ParseSoftwareBlocks(string softwarePath, ProjectData projectData, CancellationToken cancellationToken)
    {
        // Parse blocks from software units
        var blockFolders = Directory.GetDirectories(softwarePath, "*Block*", SearchOption.AllDirectories);

        foreach (var blockFolder in blockFolders)
        {
            var blockFiles = Directory.GetFiles(blockFolder, "*.xml");
            foreach (var blockFile in blockFiles)
            {
                var block = await ParseBlockFile(blockFile, cancellationToken);
                if (block != null)
                {
                    projectData.Blocks.Add(block);
                }
            }
        }

        // Also look for SCL, LAD, FBD files
        var sourceFiles = Directory.GetFiles(softwarePath, "*.scl", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(softwarePath, "*.lad", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(softwarePath, "*.fbd", SearchOption.AllDirectories));

        foreach (var sourceFile in sourceFiles)
        {
            var block = ParseSourceFile(sourceFile);
            if (block != null)
            {
                projectData.Blocks.Add(block);
            }
        }
    }

    private async Task<BlockInfo?> ParseBlockFile(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            var doc = XDocument.Parse(content);

            var sw = doc.Element("Document")?.Element("SW.Blocks.CompileUnit") ??
                    doc.Element("Document")?.Element("SW.Blocks.FB") ??
                    doc.Element("Document")?.Element("SW.Blocks.FC") ??
                    doc.Element("Document")?.Element("SW.Blocks.OB") ??
                    doc.Element("Document")?.Element("SW.Blocks.DB") ??
                    doc.Root;

            if (sw == null)
                return null;

            var blockInfo = sw.Element("AttributeList")?.Element("Name")?.Value ??
                           sw.Element("ObjectList")?.Element("SW.Blocks.CompileUnit")?.Element("AttributeList")?.Element("Name")?.Value;

            if (string.IsNullOrEmpty(blockInfo))
            {
                blockInfo = Path.GetFileNameWithoutExtension(filePath);
            }

            // Extract block type and number
            var blockType = DetermineBlockType(blockInfo, filePath);
            var blockNumber = ExtractBlockNumber(blockInfo);

            var block = new BlockInfo
            {
                Name = blockInfo,
                Number = blockNumber,
                Type = blockType,
                Language = DetermineLanguage(sw),
                Title = sw.Element("AttributeList")?.Elements("Title").FirstOrDefault()?.Value,
                Comment = sw.Element("AttributeList")?.Elements("Comment").FirstOrDefault()?.Value,
                NetworkCount = CountNetworks(sw),
                SizeBytes = content.Length,
                Properties = new Dictionary<string, string>
                {
                    ["FileName"] = Path.GetFileName(filePath)
                }
            };

            // Parse interface
            var interfaceElement = sw.Element("Interface");
            if (interfaceElement != null)
            {
                ParseInterface(interfaceElement, block);
            }

            return block;
        }
        catch
        {
            return null;
        }
    }

    private void ParseInterface(XElement interfaceElement, BlockInfo block)
    {
        var sections = new[] { "Input", "Output", "InOut", "Static", "Temp", "Constant", "Return" };

        foreach (var section in sections)
        {
            var sectionElement = interfaceElement.Element(section);
            if (sectionElement != null)
            {
                var members = sectionElement.Elements("Member");
                var count = members.Count();
                if (count > 0)
                {
                    block.Interface[section] = count.ToString();
                }
            }
        }
    }

    private BlockInfo? ParseSourceFile(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath).ToLower();

        var language = extension switch
        {
            ".scl" => "SCL",
            ".lad" => "LAD",
            ".fbd" => "FBD",
            ".stl" => "STL",
            _ => "Unknown"
        };

        var blockType = DetermineBlockType(fileName, filePath);
        var blockNumber = ExtractBlockNumber(fileName);

        return new BlockInfo
        {
            Name = fileName,
            Number = blockNumber,
            Type = blockType,
            Language = language,
            Properties = new Dictionary<string, string>
            {
                ["FileName"] = Path.GetFileName(filePath),
                ["SourceFile"] = "true"
            }
        };
    }

    private string DetermineBlockType(string blockName, string filePath)
    {
        var upper = blockName.ToUpper();
        var pathUpper = filePath.ToUpper();

        if (upper.StartsWith("OB") || pathUpper.Contains("\\OB")) return "OB";
        if (upper.StartsWith("FB") || pathUpper.Contains("\\FB")) return "FB";
        if (upper.StartsWith("FC") || pathUpper.Contains("\\FC")) return "FC";
        if (upper.StartsWith("DB") || pathUpper.Contains("\\DB")) return "DB";
        if (upper.StartsWith("UDT") || pathUpper.Contains("\\UDT")) return "UDT";
        if (upper.StartsWith("SDB") || pathUpper.Contains("\\SDB")) return "SDB";
        if (upper.StartsWith("SFB") || pathUpper.Contains("\\SFB")) return "SFB";
        if (upper.StartsWith("SFC") || pathUpper.Contains("\\SFC")) return "SFC";

        return "Block";
    }

    private int ExtractBlockNumber(string blockName)
    {
        var match = System.Text.RegularExpressions.Regex.Match(blockName, @"\d+");
        if (match.Success && int.TryParse(match.Value, out var number))
        {
            return number;
        }
        return 0;
    }

    private string DetermineLanguage(XElement element)
    {
        var languageAttr = element.Attribute("ProgrammingLanguage")?.Value;
        if (!string.IsNullOrEmpty(languageAttr))
            return languageAttr;

        // Check for language-specific elements
        if (element.Descendants("SW.Blocks.CompileUnit").Any(e => e.Attribute("ProgrammingLanguage")?.Value == "LAD"))
            return "LAD";
        if (element.Descendants("SW.Blocks.CompileUnit").Any(e => e.Attribute("ProgrammingLanguage")?.Value == "FBD"))
            return "FBD";
        if (element.Descendants("SW.Blocks.CompileUnit").Any(e => e.Attribute("ProgrammingLanguage")?.Value == "SCL"))
            return "SCL";
        if (element.Descendants("SW.Blocks.CompileUnit").Any(e => e.Attribute("ProgrammingLanguage")?.Value == "STL"))
            return "STL";
        if (element.Descendants("SW.Blocks.CompileUnit").Any(e => e.Attribute("ProgrammingLanguage")?.Value == "GRAPH"))
            return "GRAPH";

        return "LAD"; // Default
    }

    private int CountNetworks(XElement element)
    {
        return element.Descendants("SW.Blocks.CompileUnit").Count() +
               element.Descendants("Network").Count() +
               element.Descendants("SW.Blocks.Network").Count();
    }

    private async Task ParseTagTables(string tagTablePath, ProjectData projectData, CancellationToken cancellationToken)
    {
        var tagFiles = Directory.GetFiles(tagTablePath, "*.xml", SearchOption.AllDirectories);

        foreach (var tagFile in tagFiles)
        {
            var tags = await ParseTagFile(tagFile, cancellationToken);
            projectData.Tags.AddRange(tags);
        }
    }

    private async Task<List<TagInfo>> ParseTagFile(string filePath, CancellationToken cancellationToken)
    {
        var tags = new List<TagInfo>();

        try
        {
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            var doc = XDocument.Parse(content);

            var tagElements = doc.Descendants("SW.Tags.PlcTag")
                .Concat(doc.Descendants("Plc.Tag"))
                .Concat(doc.Descendants("Tag"));

            foreach (var tagElement in tagElements)
            {
                var tag = new TagInfo
                {
                    Name = tagElement.Element("Name")?.Value ??
                          tagElement.Attribute("Name")?.Value ?? "Unknown",
                    DataType = tagElement.Element("DataType")?.Value ??
                              tagElement.Attribute("DataType")?.Value ?? "",
                    Address = tagElement.Element("LogicalAddress")?.Value ??
                             tagElement.Element("Address")?.Value,
                    Description = tagElement.Element("Comment")?.Value,
                    Scope = DetermineTagScope(filePath),
                    Properties = new Dictionary<string, string>()
                };

                // Check if it's an I/O tag
                if (!string.IsNullOrEmpty(tag.Address))
                {
                    tag.IsIoTag = tag.Address.StartsWith("I") || tag.Address.StartsWith("Q");
                }

                // Extract additional attributes
                var attributes = tagElement.Elements("AttributeList")?.Elements();
                if (attributes != null)
                {
                    foreach (var attr in attributes)
                    {
                        tag.Properties[attr.Name.LocalName] = attr.Value;
                    }
                }

                tags.Add(tag);
            }
        }
        catch { }

        return tags;
    }

    internal static string DetermineTagScope(string filePath)
    {
        // Only look at the file name and its folder. The full path includes the
        // random temp extraction directory (tia_{guid}), whose hex digits often
        // contain "fb"/"fc" and made the scope nondeterministic.
        var fileName = Path.GetFileName(filePath);
        var folder = Path.GetFileName(Path.GetDirectoryName(filePath) ?? string.Empty);
        var segments = new[] { fileName, folder };

        if (segments.Any(s => s.Contains("PlcTagTable", StringComparison.OrdinalIgnoreCase)))
            return "Global";
        if (segments.Any(s => s.Contains("DataBlock", StringComparison.OrdinalIgnoreCase)))
            return "DB";
        if (segments.Any(s => BlockToken("FB").IsMatch(s)))
            return "FB";
        if (segments.Any(s => BlockToken("FC").IsMatch(s)))
            return "FC";
        return "Local";
    }

    // Matches FB/FC as a standalone token (e.g. "FB10", "My_FC.xml"), not inside words.
    private static Regex BlockToken(string prefix) =>
        new($@"(^|[^A-Za-z]){prefix}(\d|[^A-Za-z]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private void ParseHardwareConfiguration(string hwPath, ProjectData projectData)
    {
        var hwFiles = Directory.GetFiles(hwPath, "*.xml", SearchOption.AllDirectories);

        foreach (var hwFile in hwFiles)
        {
            try
            {
                var content = File.ReadAllText(hwFile);
                var doc = XDocument.Parse(content);

                var devices = doc.Descendants("Device")
                    .Concat(doc.Descendants("DeviceItem"));

                foreach (var device in devices)
                {
                    var typeName = device.Attribute("TypeName")?.Value ?? device.Element("TypeName")?.Value;
                    if (!string.IsNullOrEmpty(typeName) && IsIoModule(typeName))
                    {
                        var module = new IoModuleInfo
                        {
                            Name = device.Attribute("Name")?.Value ?? typeName,
                            Type = DetermineModuleType(typeName),
                            CatalogNumber = device.Attribute("TypeIdentifier")?.Value ?? typeName,
                            Vendor = "Siemens",
                            Location = device.Attribute("PositionNumber")?.Value,
                            Properties = new Dictionary<string, string>
                            {
                                ["TypeName"] = typeName
                            }
                        };

                        // Extract channel count if available
                        var channels = device.Element("Channels")?.Elements().Count() ?? 0;
                        if (channels > 0)
                        {
                            module.ChannelCount = channels;
                        }

                        projectData.Modules.Add(module);
                    }
                }
            }
            catch { }
        }
    }

    private bool IsIoModule(string typeName)
    {
        var ioKeywords = new[] { "DI", "DO", "DQ", "AI", "AO", "AQ", "SM", "IM", "FM", "CM", "CP" };
        return ioKeywords.Any(kw => typeName.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }

    private string DetermineModuleType(string typeName)
    {
        var upper = typeName.ToUpper();

        if (upper.Contains("DI")) return "Digital Input";
        if (upper.Contains("DO") || upper.Contains("DQ")) return "Digital Output";
        if (upper.Contains("AI")) return "Analog Input";
        if (upper.Contains("AO") || upper.Contains("AQ")) return "Analog Output";
        if (upper.Contains("SM")) return "Signal Module";
        if (upper.Contains("IM")) return "Interface Module";
        if (upper.Contains("FM")) return "Function Module";
        if (upper.Contains("CM")) return "Communication Module";
        if (upper.Contains("CP")) return "Communication Processor";

        return "I/O Module";
    }

    private class ProjectData
    {
        public string ProjectName { get; set; } = string.Empty;
        public string Language { get; set; } = "en-US";
        public ControllerInfo? Controller { get; set; }
        public List<BlockInfo> Blocks { get; } = new();
        public List<ProgramInfo> Programs { get; } = new();
        public List<TagInfo> Tags { get; } = new();
        public List<IoModuleInfo> Modules { get; } = new();
    }
}