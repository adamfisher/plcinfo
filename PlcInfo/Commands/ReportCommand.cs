using CommandLine;
using PlcInfo.Parsing;
using PlcInfo.Rendering;
using PlcInfo.Services;

namespace PlcInfo.Commands;

[Verb("report", HelpText = "Write a self-contained HTML report with rendered ladder logic, lint findings, tags and data types.")]
public class ReportCommand : BaseFileCommand
{
    public ReportCommand()
    {
        Format = OutputFormat.Html;
    }

    protected override async Task<int> RunAsync(IPlcParserRegistry parserRegistry, CancellationToken cancellationToken)
    {
        if (Format != OutputFormat.Html)
            throw new UsageException("report: only the html format is supported.");

        var project = await LoadProjectAsync(parserRegistry, FilePath, cancellationToken);
        var findings = new LintService().Analyze(project);
        await WriteResultAsync(HtmlReportRenderer.RenderProject(project, findings), cancellationToken);
        return ExitCodes.Success;
    }
}
