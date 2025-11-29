using CommandLine;
using PlcInfo.Services;
using PlcInfo.Parsing;

namespace PlcInfo.Commands;

[Verb("diff", HelpText = "Compare two project files")]
public class DiffCommand
{
    [Value(0, Required = true, HelpText = "First PLC project file", MetaName = "FILE1")]
    public string File1 { get; set; } = string.Empty;

    [Value(1, Required = true, HelpText = "Second PLC project file", MetaName = "FILE2")]
    public string File2 { get; set; } = string.Empty;

    [Option('f', "format", Required = false, Default = OutputFormat.Text, HelpText = "Output format (text, json, yaml, md)")]
    public OutputFormat Format { get; set; } = OutputFormat.Text;

    public async Task<int> Execute(IPlcParserRegistry parserRegistry)
    {
        try
        {
            var parser1 = parserRegistry.ResolveForFile(File1);
            var project1 = await parser1.ParseAsync(File1);

            var parser2 = parserRegistry.ResolveForFile(File2);
            var project2 = await parser2.ParseAsync(File2);

            var diffService = new PlcDiffService();
            var result = diffService.Compare(project1, project2);

            var formatter = FormatterHelper.GetFormatter(Format);
            Console.WriteLine(formatter.FormatDiff(result));

            return result.HasDifferences ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}