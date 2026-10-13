using CommandLine;
using PlcInfo.Parsing;

namespace PlcInfo.Commands;

[Verb("io", HelpText = "Show the I/O configuration: modules and I/O tags.")]
public class IoCommand : BaseFileCommand
{
    protected override async Task<int> RunAsync(IPlcParserRegistry parserRegistry, CancellationToken cancellationToken)
    {
        var project = await LoadProjectAsync(parserRegistry, FilePath, cancellationToken);
        await WriteResultAsync(Formatter.FormatIo(project), cancellationToken);
        return ExitCodes.Success;
    }
}
