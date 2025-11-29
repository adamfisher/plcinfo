using CommandLine;
using PlcInfo.Parsing;

namespace PlcInfo.Commands;

[Verb("summary", HelpText = "Display project summary")]
public class SummaryCommand : BaseFileCommand
{
    public async Task<int> Execute(IPlcParserRegistry parserRegistry)
    {
        try
        {
            var parser = parserRegistry.ResolveForFile(FilePath);
            var project = await parser.ParseAsync(FilePath);
            var formatter = FormatterHelper.GetFormatter(Format);
            Console.WriteLine(formatter.FormatSummary(project));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}