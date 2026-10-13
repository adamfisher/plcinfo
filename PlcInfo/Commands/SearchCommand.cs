using CommandLine;
using PlcInfo.Parsing;
using PlcInfo.Services;

namespace PlcInfo.Commands;

[Verb("search", HelpText = "Search tags, comments and instructions for text.")]
public class SearchCommand : BaseFileCommand
{
    private static readonly string[] ValidKinds = { "tag", "comment", "instruction" };

    [Value(1, Required = false, MetaName = "QUERY", HelpText = "Text to search for (case-insensitive).")]
    public string? Query { get; set; }

    // Kept for compatibility with earlier releases; QUERY is the documented form.
    [Option('t', "text", Required = false, Hidden = true)]
    public string? Text { get; set; }

    [Option('k', "kind", Required = false, MetaValue = "KIND", HelpText = "Only search one kind: tag, comment or instruction.")]
    public string? Kind { get; set; }

    /// <summary>
    /// The text to search for, from QUERY or the legacy --text option.
    /// </summary>
    public string SearchText => Query ?? Text ?? string.Empty;

    protected override async Task<int> RunAsync(IPlcParserRegistry parserRegistry, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
            throw new UsageException("search: missing QUERY.");

        var kind = NormalizeKind(Kind);
        var project = await LoadProjectAsync(parserRegistry, FilePath, cancellationToken);
        var results = new SearchService().Search(project, SearchText, kind);
        await WriteResultAsync(Formatter.FormatSearchResults(results), cancellationToken);
        return ExitCodes.Success;
    }

    private static string? NormalizeKind(string? kind)
    {
        if (kind == null)
            return null;

        // Accept plurals ("tags") as well.
        var singular = kind.EndsWith('s') ? kind[..^1] : kind;
        var match = ValidKinds.FirstOrDefault(k => k.Equals(singular, StringComparison.OrdinalIgnoreCase));
        return match ?? throw new UsageException(
            $"search: unknown --kind '{kind}'. Expected one of: {string.Join(", ", ValidKinds)}.");
    }
}
