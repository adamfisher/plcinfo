using CommandLine;
using PlcInfo.Formatters;
using PlcInfo.Parsing;

namespace PlcInfo.Commands;

[Verb("tags", HelpText = "Display project tags")]
public class TagsCommand : BaseFileCommand
{
    [Option("scope", Required = false, HelpText = "Filter by scope")]
    public string? Scope { get; set; }

    [Option("type", Required = false, HelpText = "Filter by data type")]
    public string? Type { get; set; }

    [Option("io-only", Required = false, Default = false, HelpText = "Show only I/O tags")]
    public bool IoOnly { get; set; }

    [Option("name-contains", Required = false, HelpText = "Filter tags containing this text")]
    public string? NameContains { get; set; }

    public async Task<int> Execute(IPlcParserRegistry parserRegistry)
    {
        try
        {
            var parser = parserRegistry.ResolveForFile(FilePath);
            var project = await parser.ParseAsync(FilePath);

            var filter = new TagFilter
            {
                Scope = Scope,
                Type = Type,
                IoOnly = IoOnly,
                NameContains = NameContains
            };

            var formatter = FormatterHelper.GetFormatter(Format);
            Console.WriteLine(formatter.FormatTags(project, filter));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}