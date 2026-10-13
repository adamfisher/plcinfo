using System.Globalization;
using CommandLine;
using PlcInfo.Commands;
using PlcInfo.Parsing;

namespace PlcInfo;

/// <summary>
/// Main program entry point for the PLC Information Tool.
/// </summary>
class Program
{
    /// <summary>
    /// The command verbs, in the order they appear in help.
    /// </summary>
    internal static readonly Type[] CommandTypes =
    {
        typeof(SummaryCommand),
        typeof(StructureCommand),
        typeof(TagsCommand),
        typeof(IoCommand),
        typeof(MetricsCommand),
        typeof(SearchCommand),
        typeof(DiffCommand),
        typeof(XrefCommand),
        typeof(LintCommand),
        typeof(ExportCommand),
        typeof(TextconvCommand)
    };

    /// <summary>
    /// Parses command-line arguments into one of the command verbs.
    /// </summary>
    internal static ParserResult<object> ParseArguments(string[] args)
    {
        var parser = new Parser(config =>
        {
            config.HelpWriter = null; // Disable automatic help so we can customize it
            config.EnableDashDash = true;
            // Accept "--format json" as documented, not only "--format Json".
            config.CaseInsensitiveEnumValues = true;
        });

        return parser.ParseArguments(NormalizeArguments(args), CommandTypes);
    }

    /// <summary>
    /// Accepts "-h" as a synonym for "--help", as most CLI tools do.
    /// Arguments after "--" are left untouched.
    /// </summary>
    internal static string[] NormalizeArguments(string[] args)
    {
        var result = new string[args.Length];
        var afterDashDash = false;
        for (int i = 0; i < args.Length; i++)
        {
            afterDashDash |= args[i] == "--";
            result[i] = !afterDashDash && args[i] == "-h" ? "--help" : args[i];
        }
        return result;
    }

    /// <summary>
    /// Creates the registry of all supported project parsers.
    /// </summary>
    internal static PlcParserRegistry CreateParserRegistry() => new(new IPlcProjectParser[]
    {
        new RockwellL5XParser(),
        new RockwellAcdParser(),
        new SiemensStep7Parser(),
        new SiemensTiaPortalParser(),
        new CodesysParser(),
        new TwinCatParser()
    });

    static async Task<int> Main(string[] args)
    {
        // Output is consumed by scripts and diffed in CI, so numbers and dates must
        // not change with the machine's locale (e.g. "12,5" under de-DE).
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        // Ctrl+C cancels the running command; it then exits with 130.
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        var parserRegistry = CreateParserRegistry();
        var parserResult = ParseArguments(args);

        return await parserResult.MapResult(
            (CommandBase command) => command.Execute(parserRegistry, cancellation.Token),
            errors => Task.FromResult(HelpFormatter.DisplayHelp(parserResult, errors)));
    }
}
