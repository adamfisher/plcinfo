using CommandLine;
using PlcInfo.Commands;
using PlcInfo.Parsing;

namespace PlcInfo;

/// <summary>
/// Main program entry point for the PLC Information Tool.
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        // Handle --version flag to show full help
        if (args.Length == 1 && (args[0] == "--version" || args[0] == "-v"))
        {
            return HelpFormatter.DisplayVersion();
        }

        // Initialize all parsers
        var parsers = new List<IPlcProjectParser>
        {
            new RockwellL5XParser(),
            new RockwellAcdParser(),
            new SiemensStep7Parser(),
            new SiemensTiaPortalParser(),
            new CodesysParser(),
            new TwinCatParser()
        };

        // Create parser registry
        var parserRegistry = new PlcParserRegistry(parsers);

        // Parse arguments and execute the appropriate command
        var parser = new Parser(config =>
        {
            config.HelpWriter = null; // Disable automatic help so we can customize it
            config.EnableDashDash = true;
        });

        var parserResult = parser.ParseArguments<
            SummaryCommand,
            StructureCommand,
            TagsCommand,
            IoCommand,
            MetricsCommand,
            SearchCommand,
            DiffCommand,
            ExportCommand
        >(args);

        return await parserResult.MapResult(
            async (SummaryCommand cmd) => await cmd.Execute(parserRegistry),
            async (StructureCommand cmd) => await cmd.Execute(parserRegistry),
            async (TagsCommand cmd) => await cmd.Execute(parserRegistry),
            async (IoCommand cmd) => await cmd.Execute(parserRegistry),
            async (MetricsCommand cmd) => await cmd.Execute(parserRegistry),
            async (SearchCommand cmd) => await cmd.Execute(parserRegistry),
            async (DiffCommand cmd) => await cmd.Execute(parserRegistry),
            async (ExportCommand cmd) => await cmd.Execute(parserRegistry),
            errors => Task.FromResult(HelpFormatter.DisplayHelp(parserResult, errors))
        );
    }
}