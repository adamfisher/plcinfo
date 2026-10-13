using System.Text.Encodings.Web;
using System.Text.Json;
using CommandLine;
using PlcInfo.Parsing;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PlcInfo.Commands;

[Verb("export", HelpText = "Export the full project model in a vendor-neutral format (json or yaml).")]
public class ExportCommand : BaseFileCommand
{
    [Option("compact", Required = false, HelpText = "Write JSON without indentation.")]
    public bool Compact { get; set; }

    // Kept for compatibility with earlier releases; prefer --compact.
    [Option('i', "indent", Required = false, Hidden = true)]
    public bool? Indent { get; set; }

    public ExportCommand()
    {
        // Export defaults to JSON rather than the human-readable text format.
        Format = OutputFormat.Json;
    }

    protected override async Task<int> RunAsync(IPlcParserRegistry parserRegistry, CancellationToken cancellationToken)
    {
        var project = await LoadProjectAsync(parserRegistry, FilePath, cancellationToken);

        string content = Format switch
        {
            OutputFormat.Json => JsonSerializer.Serialize(project, new JsonSerializerOptions
            {
                WriteIndented = Indent ?? !Compact,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }),
            OutputFormat.Yaml => new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
                .WithQuotingNecessaryStrings()
                .Build()
                .Serialize(project),
            _ => throw new UsageException($"export: --format must be json or yaml (got '{Format.ToString().ToLowerInvariant()}').")
        };

        await WriteResultAsync(content, cancellationToken);
        return ExitCodes.Success;
    }
}
