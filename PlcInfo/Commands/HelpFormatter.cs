using System.Reflection;
using CommandLine;
using CommandLine.Text;

namespace PlcInfo.Commands;

/// <summary>
/// Formats help, version and usage-error output.
/// Uses CommandLine's AutoBuild to generate command/option docs from attributes.
/// </summary>
public static class HelpFormatter
{
    /// <summary>
    /// The tool version, from the assembly's informational version without build metadata.
    /// </summary>
    public static string Version
    {
        get
        {
            var assembly = typeof(HelpFormatter).Assembly;
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString()
                ?? "0.0.0";
            var plus = version.IndexOf('+');
            return plus >= 0 ? version[..plus] : version;
        }
    }

    /// <summary>
    /// Writes help, version or usage errors and returns the exit code:
    /// 0 when help or the version was requested, 2 for usage errors.
    /// </summary>
    public static int DisplayHelp<T>(ParserResult<T> result, IEnumerable<Error> errors)
    {
        var errorList = errors.ToList();

        if (errorList.Any(e => e is VersionRequestedError))
        {
            Console.WriteLine($"plcinfo {Version}");
            return ExitCodes.Success;
        }

        // No verb (or an unknown one) was given, so list the verbs rather than
        // the options of a single verb.
        var showVerbs = errorList.Any(e =>
            e is NoVerbSelectedError or BadVerbSelectedError ||
            e is HelpVerbRequestedError { Matched: false });

        var helpRequested = errorList.Any(e => e is HelpRequestedError or HelpVerbRequestedError);
        var verb = result.TypeInfo.Current.GetCustomAttribute<VerbAttribute>()?.Name;

        var helpText = HelpText.AutoBuild(result, h =>
        {
            h.AdditionalNewLineAfterOption = false;
            h.AddDashesToOption = true;
            h.Heading = $"plcinfo {Version}";
            h.Copyright = "";

            if (!helpRequested)
                h = HelpText.DefaultParsingErrorsHandler(result, h);

            if (showVerbs)
            {
                h.AddPreOptionsLines([
                    "",
                    "Inspect, summarize and compare PLC projects from multiple vendors.",
                    "",
                    "Usage: plcinfo <command> [options] <file>...",
                    "",
                    "Commands:"
                ]);
                h.AddPostOptionsLines([
                    "",
                    "Examples:",
                    "  plcinfo summary project.L5X",
                    "  plcinfo tags project.L5X --io-only --format json",
                    "  plcinfo search project.L5X Motor --kind instruction",
                    "  plcinfo diff old.L5X new.L5X --format md --output changes.md",
                    "  plcinfo export project.L5X --format yaml",
                    "",
                    "Exit status: 0 success, 1 error, 2 usage error (diff: 0 same, 1 different, 2 trouble).",
                    "Run 'plcinfo <command> --help' for a command's options."
                ]);
            }
            else if (verb != null)
            {
                h.AddPreOptionsLines([
                    "",
                    $"Usage: plcinfo {verb} [options] {UsageArguments(verb)}",
                    "",
                    "Options:"
                ]);
            }

            return h;
        }, e => e, verbsIndex: showVerbs);

        if (helpRequested)
        {
            Console.WriteLine(helpText);
            return ExitCodes.Success;
        }

        Console.Error.WriteLine(helpText);
        return ExitCodes.Usage;
    }

    private static string UsageArguments(string verb) => verb switch
    {
        "diff" => "OLD NEW",
        "search" => "FILE QUERY",
        _ => "FILE"
    };
}
