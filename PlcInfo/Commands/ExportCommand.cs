using System.Text.Json;
using CommandLine;
using PlcInfo.Parsing;

namespace PlcInfo.Commands;

[Verb("export", HelpText = "Export project to neutral format")]
public class ExportCommand
{
    [Value(0, Required = true, HelpText = "PLC project file path", MetaName = "FILE")]
    public string FilePath { get; set; } = string.Empty;

    [Option('f', "format", Required = false, Default = "neutral-json", HelpText = "Export format (neutral-json)")]
    public string ExportFormat { get; set; } = "neutral-json";

    [Option('o', "output", Required = false, HelpText = "Output file path (default: stdout)")]
    public string? OutputPath { get; set; }

    // Nullable so the option takes a value: a plain bool is a presence-only switch,
    // which made "--indent false" impossible.
    [Option('i', "indent", Required = false, HelpText = "Indent JSON output: true or false (default: true)")]
    public bool? Indent { get; set; }

    public async Task<int> Execute(IPlcParserRegistry parserRegistry)
    {
        try
        {
            var parser = parserRegistry.ResolveForFile(FilePath);
            var project = await parser.ParseAsync(FilePath);

            if (ExportFormat.Equals("neutral-json", StringComparison.OrdinalIgnoreCase))
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = Indent ?? true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var json = JsonSerializer.Serialize(project, options);

                if (string.IsNullOrEmpty(OutputPath))
                {
                    Console.WriteLine(json);
                }
                else
                {
                    await File.WriteAllTextAsync(OutputPath, json);
                    Console.WriteLine($"Exported to: {OutputPath}");
                }
            }
            else
            {
                Console.Error.WriteLine($"Unsupported export format: {ExportFormat}");
                return 1;
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}