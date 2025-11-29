using CommandLine;
using PlcInfo.Parsing;
using PlcInfo.Services;

namespace PlcInfo.Commands;

[Verb("search", HelpText = "Search within project")]
public class SearchCommand : BaseFileCommand
{
    [Option('t', "text", Required = true, HelpText = "Text to search for")]
    public string SearchText { get; set; } = string.Empty;

    [Option('k', "kind", Required = false, HelpText = "Search kind (tag, comment, instruction)")]
    public string? Kind { get; set; }

    public async Task<int> Execute(IPlcParserRegistry parserRegistry)
    {
        try
        {
            var parser = parserRegistry.ResolveForFile(FilePath);
            var project = await parser.ParseAsync(FilePath);

            var searchService = new SearchService();
            var results = searchService.Search(project, SearchText, Kind);

            var formatter = FormatterHelper.GetFormatter(Format);
            Console.WriteLine(formatter.FormatSearchResults(results));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}