using CommandLine;
using PlcInfo.Parsing;
using PlcInfo.Services;

namespace PlcInfo.Commands;

[Verb("diff", HelpText = "Compare two projects. Exit status: 0 identical, 1 different, 2 trouble.")]
public class DiffCommand : CommandBase
{
    [Value(0, Required = true, MetaName = "OLD", HelpText = "Original project file.")]
    public string File1 { get; set; } = string.Empty;

    [Value(1, Required = true, MetaName = "NEW", HelpText = "Changed project file.")]
    public string File2 { get; set; } = string.Empty;

    // Like diff(1): 1 means "differences found", so errors use 2.
    protected override int ErrorExitCode => ExitCodes.Usage;

    protected override async Task<int> RunAsync(IPlcParserRegistry parserRegistry, CancellationToken cancellationToken)
    {
        var project1 = await LoadProjectAsync(parserRegistry, File1, cancellationToken);
        var project2 = await LoadProjectAsync(parserRegistry, File2, cancellationToken);

        var result = new PlcDiffService().Compare(project1, project2);
        await WriteResultAsync(Formatter.FormatDiff(result), cancellationToken);

        return result.HasDifferences ? ExitCodes.Differences : ExitCodes.Success;
    }
}
