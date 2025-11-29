using System.Diagnostics;
using System.IO.Compression;
using PlcInfo.Models;

namespace PlcInfo.Parsing;

/// <summary>
/// Parser for Rockwell ACD files (Studio 5000 compressed format).
/// </summary>
public class RockwellAcdParser : IPlcProjectParser
{
    private readonly RockwellL5XParser _l5xParser = new();

    /// <inheritdoc/>
    public bool CanParse(string filePath)
    {
        return Path.GetExtension(filePath).Equals(".ACD", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public async Task<PlcProject> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        // First try to extract using external tool if available
        var extractedL5x = await TryExtractWithExternalTool(filePath, cancellationToken);

        if (!string.IsNullOrEmpty(extractedL5x))
        {
            try
            {
                // Parse the extracted L5X
                var project = await _l5xParser.ParseAsync(extractedL5x, cancellationToken);

                // Update metadata
                project = project with
                {
                    Format = "ACD",
                    FilePath = filePath,
                    LastModified = File.GetLastWriteTime(filePath)
                };

                // Clean up temp file
                if (File.Exists(extractedL5x) && extractedL5x.Contains("temp", StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(extractedL5x); } catch { }
                }

                return project;
            }
            catch (Exception ex)
            {
                // If L5X parsing fails, continue with other methods
                Console.Error.WriteLine($"Failed to parse extracted L5X: {ex.Message}");
            }
        }

        // Try to read as ZIP and find L5X content
        try
        {
            using var archive = ZipFile.OpenRead(filePath);

            // Look for L5X files in the archive
            var l5xEntry = archive.Entries.FirstOrDefault(e =>
                Path.GetExtension(e.FullName).Equals(".L5X", StringComparison.OrdinalIgnoreCase));

            if (l5xEntry != null)
            {
                var tempPath = Path.Combine(Path.GetTempPath(), $"temp_{Guid.NewGuid()}.L5X");
                try
                {
                    l5xEntry.ExtractToFile(tempPath, true);
                    var project = await _l5xParser.ParseAsync(tempPath, cancellationToken);

                    return project with
                    {
                        Format = "ACD",
                        FilePath = filePath,
                        LastModified = File.GetLastWriteTime(filePath)
                    };
                }
                finally
                {
                    if (File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { }
                    }
                }
            }

            // If no L5X found, look for XML content
            var xmlEntry = archive.Entries.FirstOrDefault(e =>
                e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                e.FullName.Contains("Controller", StringComparison.OrdinalIgnoreCase));

            if (xmlEntry != null)
            {
                using var stream = xmlEntry.Open();
                using var reader = new StreamReader(stream);
                var content = await reader.ReadToEndAsync(cancellationToken);

                if (content.Contains("<Controller") || content.Contains("<RSLogix5000Content"))
                {
                    var tempPath = Path.Combine(Path.GetTempPath(), $"temp_{Guid.NewGuid()}.L5X");
                    try
                    {
                        await File.WriteAllTextAsync(tempPath, content, cancellationToken);
                        var project = await _l5xParser.ParseAsync(tempPath, cancellationToken);

                        return project with
                        {
                            Format = "ACD",
                            FilePath = filePath,
                            LastModified = File.GetLastWriteTime(filePath)
                        };
                    }
                    finally
                    {
                        if (File.Exists(tempPath))
                        {
                            try { File.Delete(tempPath); } catch { }
                        }
                    }
                }
            }
        }
        catch (InvalidDataException)
        {
            // Not a valid ZIP file, try other methods
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to read ACD as ZIP: {ex.Message}");
        }

        // If all extraction methods fail, return minimal project
        return CreateMinimalProject(filePath);
    }

    private async Task<string?> TryExtractWithExternalTool(string filePath, CancellationToken cancellationToken)
    {
        // Try to find acdmerge.exe or similar tool
        var toolPaths = new[]
        {
            @"C:\Program Files (x86)\Rockwell Software\RSLogix 5000 Tools\acdmerge.exe",
            @"C:\Program Files\Rockwell Software\RSLogix 5000 Tools\acdmerge.exe",
            @"C:\Program Files (x86)\Rockwell Software\Studio 5000\acdmerge.exe",
            @"C:\Program Files\Rockwell Software\Studio 5000\acdmerge.exe",
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "acdmerge.exe")
        };

        string? acdMergePath = toolPaths.FirstOrDefault(File.Exists);

        if (string.IsNullOrEmpty(acdMergePath))
        {
            // Check if it's in PATH
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "where",
                        Arguments = "acdmerge.exe",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var result = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(result))
                {
                    acdMergePath = result.Trim().Split('\n')[0].Trim();
                }
            }
            catch { }
        }

        if (string.IsNullOrEmpty(acdMergePath))
            return null;

        // Try to extract using the tool
        var outputPath = Path.Combine(Path.GetTempPath(), $"temp_{Guid.NewGuid()}.L5X");

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = acdMergePath,
                    Arguments = $"\"{filePath}\" \"{outputPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0 && File.Exists(outputPath))
            {
                return outputPath;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to extract ACD with external tool: {ex.Message}");
        }

        if (File.Exists(outputPath))
        {
            try { File.Delete(outputPath); } catch { }
        }

        return null;
    }

    private PlcProject CreateMinimalProject(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);

        return new PlcProject
        {
            Vendor = "Rockwell Automation",
            Format = "ACD",
            FilePath = filePath,
            Controller = new ControllerInfo
            {
                Name = fileName,
                Type = "Unknown",
                Vendor = "Rockwell Automation",
                Properties = new Dictionary<string, string>
                {
                    ["Note"] = "ACD file could not be fully extracted. Limited information available."
                }
            },
            LastModified = File.GetLastWriteTime(filePath),
            Metadata = new Dictionary<string, string>
            {
                ["ParseStatus"] = "Minimal",
                ["Reason"] = "Could not extract L5X content from ACD file"
            }
        };
    }
}