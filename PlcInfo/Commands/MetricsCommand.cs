using CommandLine;
using PlcInfo.Parsing;
using PlcInfo.Services;

namespace PlcInfo.Commands;

[Verb("metrics", HelpText = "Display project metrics")]
public class MetricsCommand : BaseFileCommand
{
    public async Task<int> Execute(IPlcParserRegistry parserRegistry)
    {
        try
        {
            var parser = parserRegistry.ResolveForFile(FilePath);
            var project = await parser.ParseAsync(FilePath);

            var metricsService = new MetricsService();
            var metrics = metricsService.CalculateMetrics(project);

            var formatter = FormatterHelper.GetFormatter(Format);
            Console.WriteLine(formatter.FormatMetrics(metrics));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}