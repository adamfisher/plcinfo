using CommandLine;
using PlcInfo.Formatters;
using PlcInfo.Parsing;
using PlcInfo.Services;

namespace PlcInfo.Commands;

[Verb("xref", HelpText = "Cross-reference: where each tag is read and written.")]
public class XrefCommand : BaseFileCommand
{
    [Value(1, Required = false, MetaName = "TAG", HelpText = "Only this tag (base name, case-insensitive; '*' wildcards allowed).")]
    public string? Tag { get; set; }

    [Option("writes", Required = false, HelpText = "Only show writes (useful to find who sets an output).")]
    public bool WritesOnly { get; set; }

    protected override async Task<int> RunAsync(IPlcParserRegistry parserRegistry, CancellationToken cancellationToken)
    {
        var project = await LoadProjectAsync(parserRegistry, FilePath, cancellationToken);
        IEnumerable<XrefEntry> entries = new XrefService().Build(project);

        if (!string.IsNullOrEmpty(Tag))
        {
            var pattern = "^" + System.Text.RegularExpressions.Regex.Escape(Tag).Replace("\\*", ".*") + "$";
            var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            entries = entries.Where(e => regex.IsMatch(e.Tag));
        }
        if (WritesOnly)
            entries = entries.Where(e => e.Access == "Write");

        await WriteResultAsync(ReportFormatter.FormatXref(entries.ToList(), Format), cancellationToken);
        return ExitCodes.Success;
    }
}
