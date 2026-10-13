using CommandLine;
using PlcInfo.Formatters;
using PlcInfo.Parsing;

namespace PlcInfo.Commands;

[Verb("textconv", HelpText = "Print the project as stable, line-oriented text for git diff (git textconv driver).")]
public class TextconvCommand : BaseFileCommand
{
    protected override async Task<int> RunAsync(IPlcParserRegistry parserRegistry, CancellationToken cancellationToken)
    {
        if (Format != OutputFormat.Text)
            throw new UsageException("textconv: only the text format is supported.");

        // Git runs textconv on every revision it shows; warnings would be noise there.
        Quiet = true;
        var project = await LoadProjectAsync(parserRegistry, FilePath, cancellationToken);
        await WriteResultAsync(CanonicalTextRenderer.Render(project), cancellationToken);
        return ExitCodes.Success;
    }
}
