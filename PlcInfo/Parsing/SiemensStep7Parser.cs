using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using PlcInfo.Models;

namespace PlcInfo.Parsing;

/// <summary>
/// Parser for Siemens Step7 project files (.s7p archives or project folders).
/// </summary>
public class SiemensStep7Parser : IPlcProjectParser
{
    /// <inheritdoc/>
    public bool CanParse(string filePath)
    {
        // Check by extension first
        if (Path.GetExtension(filePath).Equals(".s7p", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // If it's a directory, check for Step7 project structure
        if (Directory.Exists(filePath))
        {
            return Directory.Exists(Path.Combine(filePath, "ombstx")) ||
                   Directory.Exists(Path.Combine(filePath, "s7asrcom")) ||
                   Directory.Exists(Path.Combine(filePath, "s7prog")) ||
                   File.Exists(Path.Combine(filePath, "s7hstatx.dir"));
        }

        return false;
    }

    /// <inheritdoc/>
    public async Task<PlcProject> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        string projectPath;
        bool isTemporary = false;

        if (File.Exists(filePath) && Path.GetExtension(filePath).Equals(".s7p", StringComparison.OrdinalIgnoreCase))
        {
            // Extract archive to temp directory
            projectPath = Path.Combine(Path.GetTempPath(), $"step7_{Guid.NewGuid()}");
            isTemporary = true;

            try
            {
                ZipFile.ExtractToDirectory(filePath, projectPath);
            }
            catch
            {
                // Try as regular archive if not ZIP
                Directory.CreateDirectory(projectPath);
                // Placeholder for other archive extraction methods
            }
        }
        else if (Directory.Exists(filePath))
        {
            projectPath = filePath;
        }
        else
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        try
        {
            var blocks = await ParseBlocks(projectPath, cancellationToken);
            var symbols = await ParseSymbols(projectPath, cancellationToken);
            var hardware = ParseHardware(projectPath);

            var controller = ExtractControllerInfo(projectPath, hardware);
            var tags = ConvertSymbolsToTags(symbols);
            var modules = ExtractIoModules(hardware);

            return new PlcProject
            {
                Vendor = "Siemens",
                Format = "Step 7 Classic",
                FilePath = filePath,
                Controller = controller,
                Blocks = blocks,
                Tags = tags,
                IoModules = modules,
                LastModified = File.Exists(filePath) ? File.GetLastWriteTime(filePath) : DateTime.Now,
                Metadata = new Dictionary<string, string>
                {
                    ["ProjectPath"] = projectPath,
                    ["Version"] = DetectStep7Version(projectPath)
                }
            };
        }
        finally
        {
            if (isTemporary && Directory.Exists(projectPath))
            {
                try
                {
                    Directory.Delete(projectPath, true);
                }
                catch { }
            }
        }
    }

    private async Task<IReadOnlyList<BlockInfo>> ParseBlocks(string projectPath, CancellationToken cancellationToken)
    {
        var blocks = new List<BlockInfo>();
        var blocksPath = Path.Combine(projectPath, "s7prog", "BLOCKS");

        if (!Directory.Exists(blocksPath))
        {
            // Try alternate paths
            blocksPath = Path.Combine(projectPath, "ombstx", "offline", "00000001");
            if (!Directory.Exists(blocksPath))
            {
                return blocks;
            }
        }

        // Parse .dbf files (block database files)
        var dbfFiles = Directory.GetFiles(blocksPath, "*.dbf", SearchOption.AllDirectories);
        foreach (var dbfFile in dbfFiles)
        {
            var blockInfo = await ParseBlockFile(dbfFile, cancellationToken);
            if (blockInfo != null)
            {
                blocks.Add(blockInfo);
            }
        }

        // Also look for .awl files (STL source files)
        var awlFiles = Directory.GetFiles(blocksPath, "*.awl", SearchOption.AllDirectories);
        foreach (var awlFile in awlFiles)
        {
            var blockInfo = await ParseAwlFile(awlFile, cancellationToken);
            if (blockInfo != null)
            {
                blocks.Add(blockInfo);
            }
        }

        // Look for block info in subfolders
        var subblkPath = Path.Combine(blocksPath, "SUBBLK");
        if (Directory.Exists(subblkPath))
        {
            var subblkFiles = Directory.GetFiles(subblkPath, "*.*", SearchOption.AllDirectories);
            foreach (var file in subblkFiles)
            {
                var blockInfo = ParseBlockFromFilename(Path.GetFileName(file));
                if (blockInfo != null && !blocks.Any(b => b.Name == blockInfo.Name))
                {
                    blocks.Add(blockInfo);
                }
            }
        }

        return blocks;
    }

    private async Task<BlockInfo?> ParseBlockFile(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllBytesAsync(filePath, cancellationToken);
            var fileName = Path.GetFileNameWithoutExtension(filePath);

            // Extract block type and number from filename
            var match = Regex.Match(fileName, @"(OB|FB|FC|DB|SDB|SFC|SFB)(\d+)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return ParseBlockFromFilename(fileName);
            }

            var blockType = match.Groups[1].Value.ToUpper();
            var blockNumber = int.Parse(match.Groups[2].Value);

            return new BlockInfo
            {
                Name = $"{blockType}{blockNumber}",
                Number = blockNumber,
                Type = blockType,
                Language = "STL", // Default for Step7
                SizeBytes = content.Length,
                Properties = new Dictionary<string, string>
                {
                    ["FileName"] = Path.GetFileName(filePath)
                }
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task<BlockInfo?> ParseAwlFile(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath, Encoding.GetEncoding(1252), cancellationToken);
            var lines = content.Split('\n');

            string? blockType = null;
            int? blockNumber = null;
            string? title = null;
            int networkCount = 0;
            string language = "STL";

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                // Parse block header
                if (trimmed.StartsWith("ORGANIZATION_BLOCK", StringComparison.OrdinalIgnoreCase))
                {
                    blockType = "OB";
                    var match = Regex.Match(trimmed, @"OB\s*(\d+)");
                    if (match.Success)
                        blockNumber = int.Parse(match.Groups[1].Value);
                }
                else if (trimmed.StartsWith("FUNCTION_BLOCK", StringComparison.OrdinalIgnoreCase))
                {
                    blockType = "FB";
                    var match = Regex.Match(trimmed, @"FB\s*(\d+)");
                    if (match.Success)
                        blockNumber = int.Parse(match.Groups[1].Value);
                }
                else if (trimmed.StartsWith("FUNCTION", StringComparison.OrdinalIgnoreCase) && !trimmed.Contains("FUNCTION_BLOCK"))
                {
                    blockType = "FC";
                    var match = Regex.Match(trimmed, @"FC\s*(\d+)");
                    if (match.Success)
                        blockNumber = int.Parse(match.Groups[1].Value);
                }
                else if (trimmed.StartsWith("DATA_BLOCK", StringComparison.OrdinalIgnoreCase))
                {
                    blockType = "DB";
                    var match = Regex.Match(trimmed, @"DB\s*(\d+)");
                    if (match.Success)
                        blockNumber = int.Parse(match.Groups[1].Value);
                }

                // Parse title
                if (trimmed.StartsWith("TITLE", StringComparison.OrdinalIgnoreCase))
                {
                    var titleMatch = Regex.Match(trimmed, @"TITLE\s*=\s*(.+)");
                    if (titleMatch.Success)
                        title = titleMatch.Groups[1].Value.Trim('"', ' ');
                }

                // Count networks
                if (trimmed.StartsWith("NETWORK", StringComparison.OrdinalIgnoreCase))
                {
                    networkCount++;
                }
            }

            if (blockType != null && blockNumber.HasValue)
            {
                return new BlockInfo
                {
                    Name = $"{blockType}{blockNumber}",
                    Number = blockNumber.Value,
                    Type = blockType,
                    Language = language,
                    Title = title,
                    NetworkCount = networkCount,
                    SizeBytes = content.Length,
                    Properties = new Dictionary<string, string>
                    {
                        ["FileName"] = Path.GetFileName(filePath),
                        ["SourceType"] = "AWL"
                    }
                };
            }
        }
        catch
        {
            // Ignore parse errors
        }

        return null;
    }

    private BlockInfo? ParseBlockFromFilename(string fileName)
    {
        var match = Regex.Match(fileName, @"(OB|FB|FC|DB|SDB|SFC|SFB|UDT)(\d+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var blockType = match.Groups[1].Value.ToUpper();
            var blockNumber = int.Parse(match.Groups[2].Value);

            return new BlockInfo
            {
                Name = $"{blockType}{blockNumber}",
                Number = blockNumber,
                Type = blockType,
                Language = "Unknown",
                Properties = new Dictionary<string, string>
                {
                    ["FileName"] = fileName
                }
            };
        }

        return null;
    }

    private async Task<Dictionary<string, SymbolEntry>> ParseSymbols(string projectPath, CancellationToken cancellationToken)
    {
        var symbols = new Dictionary<string, SymbolEntry>();

        // Look for symbol table files
        var symbolPaths = new[]
        {
            Path.Combine(projectPath, "s7asrcom", "s7cntref.dbf"),
            Path.Combine(projectPath, "YDBs", "SYMLIST.DBF"),
            Path.Combine(projectPath, "ombstx", "offline", "00000001", "SYMLIST.DBF")
        };

        foreach (var symbolPath in symbolPaths)
        {
            if (File.Exists(symbolPath))
            {
                try
                {
                    var content = await File.ReadAllTextAsync(symbolPath, Encoding.GetEncoding(1252), cancellationToken);
                    var lines = content.Split('\n');

                    foreach (var line in lines)
                    {
                        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3)
                        {
                            var symbol = new SymbolEntry
                            {
                                Name = parts[0].Trim('"'),
                                Address = parts[1],
                                DataType = parts[2],
                                Comment = parts.Length > 3 ? string.Join(" ", parts.Skip(3)) : null
                            };

                            symbols[symbol.Name] = symbol;
                        }
                    }
                }
                catch
                {
                    // Ignore parse errors
                }
                break;
            }
        }

        return symbols;
    }

    private Dictionary<string, string> ParseHardware(string projectPath)
    {
        var hardware = new Dictionary<string, string>();

        // Look for hardware configuration files
        var hwPaths = new[]
        {
            Path.Combine(projectPath, "hOmSave7", "s7hkcomx"),
            Path.Combine(projectPath, "h1", "STATION.CFG"),
            Path.Combine(projectPath, "XDBs", "System", "HW.CFG")
        };

        foreach (var hwPath in hwPaths)
        {
            if (Directory.Exists(hwPath))
            {
                var files = Directory.GetFiles(hwPath, "*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    if (!hardware.ContainsKey(fileName))
                    {
                        hardware[fileName] = Path.GetFileName(file);
                    }
                }
            }
            else if (File.Exists(hwPath))
            {
                try
                {
                    var content = File.ReadAllText(hwPath, Encoding.GetEncoding(1252));
                    // Simple extraction of hardware info from config files
                    var cpuMatch = Regex.Match(content, @"CPU\s*(\d+)", RegexOptions.IgnoreCase);
                    if (cpuMatch.Success)
                    {
                        hardware["CPU"] = $"CPU {cpuMatch.Groups[1].Value}";
                    }
                }
                catch { }
            }
        }

        return hardware;
    }

    private ControllerInfo ExtractControllerInfo(string projectPath, Dictionary<string, string> hardware)
    {
        var controller = new ControllerInfo
        {
            Vendor = "Siemens",
            Type = "S7-300/400",
            Properties = new Dictionary<string, string>()
        };

        // Try to determine CPU type
        if (hardware.TryGetValue("CPU", out var cpuType))
        {
            controller.CpuType = cpuType;
            controller.Type = DeterminePlcFamily(cpuType);
        }

        // Extract project name from path or metadata
        controller.Name = "S7 Controller";

        // Look for rack/slot info
        var rackMatch = hardware.Keys.FirstOrDefault(k => k.Contains("RACK", StringComparison.OrdinalIgnoreCase));
        if (rackMatch != null)
        {
            if (int.TryParse(Regex.Match(rackMatch, @"\d+").Value, out var rack))
            {
                controller.Rack = rack;
            }
        }

        controller.NetworkCount = hardware.Count(kv =>
            kv.Key.Contains("CP", StringComparison.OrdinalIgnoreCase) ||
            kv.Key.Contains("NET", StringComparison.OrdinalIgnoreCase));

        return controller;
    }

    private string DeterminePlcFamily(string cpuType)
    {
        if (cpuType.Contains("1200")) return "S7-1200";
        if (cpuType.Contains("1500")) return "S7-1500";
        if (cpuType.Contains("300")) return "S7-300";
        if (cpuType.Contains("400")) return "S7-400";
        if (cpuType.Contains("ET200")) return "ET200";
        return "S7-300/400";
    }

    private IReadOnlyList<TagInfo> ConvertSymbolsToTags(Dictionary<string, SymbolEntry> symbols)
    {
        var tags = new List<TagInfo>();

        foreach (var symbol in symbols.Values)
        {
            var tag = new TagInfo
            {
                Name = symbol.Name,
                DataType = ConvertStep7DataType(symbol.DataType),
                Address = symbol.Address,
                Description = symbol.Comment,
                Scope = "Global",
                IsIoTag = IsIoAddress(symbol.Address),
                Properties = new Dictionary<string, string>
                {
                    ["Step7DataType"] = symbol.DataType
                }
            };

            tags.Add(tag);
        }

        return tags;
    }

    private string ConvertStep7DataType(string step7Type)
    {
        return step7Type?.ToUpper() switch
        {
            "BOOL" => "BOOL",
            "BYTE" => "BYTE",
            "WORD" => "WORD",
            "DWORD" => "DWORD",
            "INT" => "INT",
            "DINT" => "DINT",
            "REAL" => "REAL",
            "TIME" => "TIME",
            "DATE" => "DATE",
            "TIME_OF_DAY" or "TOD" => "TOD",
            "DATE_AND_TIME" or "DT" => "DATE_TIME",
            "STRING" => "STRING",
            "CHAR" => "CHAR",
            "S5TIME" => "S5TIME",
            "TIMER" => "TIMER",
            "COUNTER" => "COUNTER",
            _ => step7Type ?? "UNKNOWN"
        };
    }

    private bool IsIoAddress(string? address)
    {
        if (string.IsNullOrEmpty(address))
            return false;

        // Check if it's an I/O address (I, Q, PI, PQ)
        return address.StartsWith("I", StringComparison.OrdinalIgnoreCase) ||
               address.StartsWith("Q", StringComparison.OrdinalIgnoreCase) ||
               address.StartsWith("PI", StringComparison.OrdinalIgnoreCase) ||
               address.StartsWith("PQ", StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<IoModuleInfo> ExtractIoModules(Dictionary<string, string> hardware)
    {
        var modules = new List<IoModuleInfo>();

        foreach (var hw in hardware)
        {
            if (IsIoModule(hw.Key))
            {
                var module = new IoModuleInfo
                {
                    Name = hw.Key,
                    Type = DetermineModuleType(hw.Key),
                    Vendor = "Siemens",
                    Properties = new Dictionary<string, string>
                    {
                        ["HardwareFile"] = hw.Value
                    }
                };

                // Extract channel count from name if possible
                var channelMatch = Regex.Match(hw.Key, @"(\d+)(?:DI|DO|AI|AO)");
                if (channelMatch.Success)
                {
                    module.ChannelCount = int.Parse(channelMatch.Groups[1].Value);
                }

                modules.Add(module);
            }
        }

        return modules;
    }

    private bool IsIoModule(string name)
    {
        var ioKeywords = new[] { "DI", "DO", "AI", "AO", "SM", "IM", "FM" };
        return ioKeywords.Any(kw => name.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }

    private string DetermineModuleType(string name)
    {
        var upper = name.ToUpper();

        if (upper.Contains("DI")) return "Digital Input";
        if (upper.Contains("DO")) return "Digital Output";
        if (upper.Contains("AI")) return "Analog Input";
        if (upper.Contains("AO")) return "Analog Output";
        if (upper.Contains("SM")) return "Signal Module";
        if (upper.Contains("IM")) return "Interface Module";
        if (upper.Contains("FM")) return "Function Module";
        if (upper.Contains("CP")) return "Communication Processor";

        return "I/O Module";
    }

    private string DetectStep7Version(string projectPath)
    {
        // Try to detect Step7 version from project files
        var versionFile = Path.Combine(projectPath, "Global", "Version");
        if (File.Exists(versionFile))
        {
            try
            {
                return File.ReadAllText(versionFile).Trim();
            }
            catch { }
        }

        return "5.x";
    }

    private class SymbolEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public string? Comment { get; set; }
    }
}