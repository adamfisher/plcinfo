using CommandLine;
using CommandLine.Text;

namespace PlcInfo.Commands;

/// <summary>
/// Formats and displays help text with custom sections.
/// Uses CommandLine's AutoBuild to generate command/option docs from attributes.
/// </summary>
public static class HelpFormatter
{
    public static int DisplayVersion()
    {
        Console.WriteLine("PLC Information Tool (plcinfo) v1.0.0");
        Console.WriteLine();
        Console.WriteLine("A comprehensive tool for analyzing PLC project files from multiple vendors.");
        Console.WriteLine();
        Console.WriteLine("Supported Formats:");
        Console.WriteLine("  - Rockwell: .L5X, .ACD");
        Console.WriteLine("  - Siemens: Step7 (.s7p), TIA Portal (.ap14-.ap19)");
        Console.WriteLine("  - CODESYS: .project");
        Console.WriteLine("  - Beckhoff: TwinCAT (.tsproj)");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  plcinfo <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  summary      Display project summary");
        Console.WriteLine("  structure    Display project structure");
        Console.WriteLine("  tags         Display project tags");
        Console.WriteLine("  io           Display I/O configuration");
        Console.WriteLine("  metrics      Display project metrics");
        Console.WriteLine("  search       Search within project");
        Console.WriteLine("  diff         Compare two project files");
        Console.WriteLine("  export       Export project to neutral format");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  plcinfo summary myproject.L5X");
        Console.WriteLine("  plcinfo structure myproject.ap17 --format md");
        Console.WriteLine("  plcinfo tags project.tsproj --io-only --format json");
        Console.WriteLine("  plcinfo metrics plc.project --format yaml");
        Console.WriteLine("  plcinfo search project.L5X --text \"timer\" --kind instruction");
        Console.WriteLine("  plcinfo diff old.L5X new.L5X");
        Console.WriteLine("  plcinfo export project.L5X --output project.json");
        Console.WriteLine();
        Console.WriteLine("For more information on a specific command:");
        Console.WriteLine("  plcinfo <command> --help");
        Console.WriteLine();
        return 0;
    }

    public static int DisplayHelp<T>(ParserResult<T> result, IEnumerable<Error> errors)
    {
        var errorList = errors.ToList();

        // No verb (or an unknown one) was given, so list the verbs rather than
        // the options of a single verb.
        var showVerbs = errorList.Any(e =>
            e is NoVerbSelectedError or BadVerbSelectedError ||
            e is HelpVerbRequestedError { Matched: false });

        var helpText = HelpText.AutoBuild(result, h =>
        {
            h.AdditionalNewLineAfterOption = false;
            h.Heading = "PLC Information Tool (plcinfo)";
            h.Copyright = "";
            h.AddPreOptionsLines([
                "",
                "A comprehensive tool for analyzing PLC project files from multiple vendors.",
                "",
                "Supported Formats:",
                "  - Rockwell: .L5X, .ACD",
                "  - Siemens: Step7 (.s7p), TIA Portal (.ap14-.ap19)",
                "  - CODESYS: .project",
                "  - Beckhoff: TwinCAT (.tsproj)",
                "",
                "Usage:",
                "  plcinfo <command> [options]",
                "",
                showVerbs ? "Commands:" : "Options:"
            ]);
            h.AddPostOptionsLines([
                "",
                "Examples:",
                "  plcinfo summary myproject.L5X",
                "  plcinfo structure myproject.ap17 --format md",
                "  plcinfo tags project.tsproj --io-only --format json",
                "  plcinfo metrics plc.project --format yaml",
                "  plcinfo search project.L5X --text \"timer\" --kind instruction",
                "  plcinfo diff old.L5X new.L5X",
                "  plcinfo export project.L5X --output project.json",
                "",
                "For more information on a specific command:",
                "  plcinfo <command> --help"
            ]);
            return h;
        }, e => e, verbsIndex: showVerbs);

        // Explicitly requested help is normal output; usage errors go to stderr.
        var helpRequested = errorList.Any(e =>
            e is HelpRequestedError or HelpVerbRequestedError or VersionRequestedError);

        if (helpRequested)
        {
            Console.WriteLine(helpText);
            return 0;
        }

        Console.Error.WriteLine(helpText);
        return 1;
    }
}
