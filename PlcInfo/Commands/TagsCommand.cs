using CommandLine;
using PlcInfo.Formatters;
using PlcInfo.Parsing;

namespace PlcInfo.Commands;

[Verb("tags", HelpText = "List tags, optionally filtered.")]
public class TagsCommand : BaseFileCommand
{
    [Option("scope", Required = false, MetaValue = "SCOPE", HelpText = "Only tags in SCOPE (e.g. Controller, a program or GVL name).")]
    public string? Scope { get; set; }

    [Option("type", Required = false, MetaValue = "TYPE", HelpText = "Only tags of data type TYPE.")]
    public string? Type { get; set; }

    [Option("io-only", Required = false, HelpText = "Only I/O tags.")]
    public bool IoOnly { get; set; }

    [Option("name-contains", Required = false, MetaValue = "TEXT", HelpText = "Only tags whose name contains TEXT (case-insensitive).")]
    public string? NameContains { get; set; }

    protected override async Task<int> RunAsync(IPlcParserRegistry parserRegistry, CancellationToken cancellationToken)
    {
        var project = await LoadProjectAsync(parserRegistry, FilePath, cancellationToken);
        var filter = new TagFilter
        {
            Scope = Scope,
            Type = Type,
            IoOnly = IoOnly,
            NameContains = NameContains
        };
        await WriteResultAsync(Formatter.FormatTags(project, filter), cancellationToken);
        return ExitCodes.Success;
    }
}
