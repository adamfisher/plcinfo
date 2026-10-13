using CommandLine;
using PlcInfo.Parsing;

namespace PlcInfo.Commands;

[Verb("summary", HelpText = "Show a project summary: controller, counts, languages.")]
public class SummaryCommand : BaseFileCommand
{
    protected override async Task<int> RunAsync(IPlcParserRegistry parserRegistry, CancellationToken cancellationToken)
    {
        var project = await LoadProjectAsync(parserRegistry, FilePath, cancellationToken);
        await WriteResultAsync(Formatter.FormatSummary(project), cancellationToken);
        return ExitCodes.Success;
    }
}
