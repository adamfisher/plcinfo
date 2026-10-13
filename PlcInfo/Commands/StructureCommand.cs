using CommandLine;
using PlcInfo.Parsing;

namespace PlcInfo.Commands;

[Verb("structure", HelpText = "Show tasks, programs, routines and blocks as a tree.")]
public class StructureCommand : BaseFileCommand
{
    protected override async Task<int> RunAsync(IPlcParserRegistry parserRegistry, CancellationToken cancellationToken)
    {
        var project = await LoadProjectAsync(parserRegistry, FilePath, cancellationToken);
        await WriteResultAsync(Formatter.FormatStructure(project), cancellationToken);
        return ExitCodes.Success;
    }
}
