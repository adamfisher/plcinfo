using CommandLine;
using PlcInfo.Parsing;

namespace PlcInfo.Commands;

[Verb("structure", HelpText = "Display project structure")]
public class StructureCommand : BaseFileCommand
{
    public async Task<int> Execute(IPlcParserRegistry parserRegistry)
    {
        try
        {
            var parser = parserRegistry.ResolveForFile(FilePath);
            var project = await parser.ParseAsync(FilePath);
            var formatter = FormatterHelper.GetFormatter(Format);
            Console.WriteLine(formatter.FormatStructure(project));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}