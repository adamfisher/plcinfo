using CommandLine;
using PlcInfo.Parsing;
using PlcInfo.Services;

namespace PlcInfo.Commands;

[Verb("metrics", HelpText = "Show code metrics: counts, language and data type distribution, complexity.")]
public class MetricsCommand : BaseFileCommand
{
    protected override async Task<int> RunAsync(IPlcParserRegistry parserRegistry, CancellationToken cancellationToken)
    {
        var project = await LoadProjectAsync(parserRegistry, FilePath, cancellationToken);
        var metrics = new MetricsService().CalculateMetrics(project);
        await WriteResultAsync(Formatter.FormatMetrics(metrics), cancellationToken);
        return ExitCodes.Success;
    }
}
