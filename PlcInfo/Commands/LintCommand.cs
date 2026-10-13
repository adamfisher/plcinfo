using CommandLine;
using PlcInfo.Formatters;
using PlcInfo.Parsing;
using PlcInfo.Services;

namespace PlcInfo.Commands;

[Verb("lint", HelpText = "Check for common problems (duplicate OTEs, missing JSR targets, unused tags, ...). Exit status 1 if findings reach --fail-on.")]
public class LintCommand : BaseFileCommand
{
    [Option("fail-on", Required = false, MetaValue = "LEVEL",
        HelpText = "Exit with status 1 when there are findings of this severity or worse: error (default), warning, info, never.")]
    public string FailOn { get; set; } = "error";

    [Option("ignore", Required = false, Separator = ',', MetaValue = "CODES",
        HelpText = "Comma-separated rule codes to skip, e.g. PLC006,PLC007.")]
    public IEnumerable<string> Ignore { get; set; } = Array.Empty<string>();

    protected override async Task<int> RunAsync(IPlcParserRegistry parserRegistry, CancellationToken cancellationToken)
    {
        LintSeverity? threshold = FailOn.ToLowerInvariant() switch
        {
            "error" => LintSeverity.Error,
            "warning" => LintSeverity.Warning,
            "info" => LintSeverity.Info,
            "never" => null,
            _ => throw new UsageException($"lint: --fail-on must be error, warning, info or never (got '{FailOn}').")
        };
        var ignored = Ignore.Select(c => c.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var project = await LoadProjectAsync(parserRegistry, FilePath, cancellationToken);
        var findings = new LintService().Analyze(project).Where(f => !ignored.Contains(f.Code)).ToList();

        await WriteResultAsync(ReportFormatter.FormatLint(findings, Format), cancellationToken);
        return threshold != null && findings.Any(f => f.Severity <= threshold) ? ExitCodes.Error : ExitCodes.Success;
    }
}
