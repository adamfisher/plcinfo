using CommandLine;
using FluentAssertions;
using PlcInfo.Commands;
using PlcInfo.Models;
using PlcInfo.Parsing;
using Xunit.Categories;

namespace PlcInfo.Tests.Commands;

[UnitTest]
[Collection("Console")]
public class CommandLineTests : IDisposable
{
    private readonly string _tempDirectory;

    public CommandLineTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, true);
    }

    // ---- argument parsing ----

    [Theory]
    [InlineData("json", OutputFormat.Json)]
    [InlineData("Json", OutputFormat.Json)]
    [InlineData("md", OutputFormat.Md)]
    [InlineData("markdown", OutputFormat.Md)]
    [InlineData("YAML", OutputFormat.Yaml)]
    public void FormatOption_IsCaseInsensitiveAndAcceptsAliases(string value, OutputFormat expected)
    {
        Parse<SummaryCommand>("summary", "project.L5X", "--format", value).Format.Should().Be(expected);
    }

    [Fact]
    public void FormatOption_DefaultsToText()
    {
        Parse<SummaryCommand>("summary", "project.L5X").Format.Should().Be(OutputFormat.Text);
    }

    [Fact]
    public void Export_DefaultsToJson()
    {
        Parse<ExportCommand>("export", "project.L5X").Format.Should().Be(OutputFormat.Json);
    }

    [Fact]
    public void Export_CompactFlag_AndLegacyIndentValue()
    {
        Parse<ExportCommand>("export", "project.L5X", "--compact").Compact.Should().BeTrue();
        Parse<ExportCommand>("export", "project.L5X", "--indent", "false").Indent.Should().BeFalse();
    }

    [Fact]
    public void Search_QueryIsPositional_WithLegacyTextOption()
    {
        Parse<SearchCommand>("search", "project.L5X", "Motor").SearchText.Should().Be("Motor");
        Parse<SearchCommand>("search", "project.L5X", "--text", "Motor").SearchText.Should().Be("Motor");
    }

    [Fact]
    public void CommonOptions_AreAvailableOnEveryCommand()
    {
        foreach (var verb in new[] { "summary", "structure", "tags", "io", "metrics", "export" })
        {
            var command = Parse<CommandBase>(verb, "project.L5X", "-o", "out.txt", "-q", "--verbose");
            command.OutputPath.Should().Be("out.txt", verb);
            command.Quiet.Should().BeTrue(verb);
            command.Verbose.Should().BeTrue(verb);
        }
    }

    [Fact]
    public void ShortHelpFlag_IsHelp_ButNotAfterDashDash()
    {
        PlcInfo.Program.NormalizeArguments(new[] { "summary", "-h" }).Should().Equal("summary", "--help");
        PlcInfo.Program.NormalizeArguments(new[] { "summary", "--", "-h" }).Should().Equal("summary", "--", "-h");
    }

    [Fact]
    public async Task Textconv_WritesCanonicalTextAndRejectsOtherFormats()
    {
        Parse<TextconvCommand>("textconv", "project.L5X");

        var ok = await Run(new TextconvCommand { FilePath = "x.test" }, new StubParser("noisy warning"));
        ok.ExitCode.Should().Be(ExitCodes.Success);
        ok.Stdout.Should().StartWith("# plcinfo textconv v1: Test Test");
        ok.Stderr.Should().BeEmpty("git shows textconv output for every revision; warnings would be noise");

        var json = await Run(new TextconvCommand { FilePath = "x.test", Format = OutputFormat.Json }, new StubParser());
        json.ExitCode.Should().Be(ExitCodes.Usage);
    }

    [Fact]
    public async Task Lint_ExitStatusFollowsFailOn()
    {
        // The stub project has a disabled program: one PLC008 warning, no errors.
        var parser = new StubParser(configure: p => p.Programs = new[]
        {
            new ProgramInfo { Name = "P", Properties = new Dictionary<string, string> { ["Disabled"] = "true" } }
        });

        (await Run(new LintCommand { FilePath = "x.test" }, parser)).ExitCode.Should().Be(ExitCodes.Success);
        (await Run(new LintCommand { FilePath = "x.test", FailOn = "warning" }, parser)).ExitCode.Should().Be(ExitCodes.Error);
        (await Run(new LintCommand { FilePath = "x.test", FailOn = "warning", Ignore = new[] { "PLC008" } }, parser)).ExitCode.Should().Be(ExitCodes.Success);
        (await Run(new LintCommand { FilePath = "x.test", FailOn = "sometimes" }, parser)).ExitCode.Should().Be(ExitCodes.Usage);
    }

    [Fact]
    public void Xref_TagIsOptionalPositional()
    {
        Parse<XrefCommand>("xref", "project.L5X").Tag.Should().BeNull();
        var command = Parse<XrefCommand>("xref", "project.L5X", "Motor*", "--writes");
        command.Tag.Should().Be("Motor*");
        command.WritesOnly.Should().BeTrue();
    }

    [Fact]
    public async Task Report_DefaultsToHtml_AndHtmlIsRejectedForNonVisualCommands()
    {
        Parse<ReportCommand>("report", "project.L5X").Format.Should().Be(OutputFormat.Html);

        var report = await Run(new ReportCommand { FilePath = "x.test" }, new StubParser());
        report.ExitCode.Should().Be(ExitCodes.Success);
        report.Stdout.Should().StartWith("<!doctype html>");

        var tags = await Run(new TagsCommand { FilePath = "x.test", Format = OutputFormat.Html }, new StubParser());
        tags.ExitCode.Should().Be(ExitCodes.Usage);
    }

    // ---- help, version and usage errors ----

    [Fact]
    public void Version_PrintsNameAndVersionOnly()
    {
        var (exitCode, stdout, _) = RunHelp("--version");

        exitCode.Should().Be(ExitCodes.Success);
        stdout.Trim().Should().MatchRegex(@"^plcinfo \d+\.\d+\.\d+\S*$");
    }

    [Fact]
    public void Help_ForVerb_ExitsZeroOnStdoutWithUsageLine()
    {
        var (exitCode, stdout, _) = RunHelp("search", "-h");

        exitCode.Should().Be(ExitCodes.Success);
        stdout.Should().Contain("Usage: plcinfo search [options] FILE QUERY");
        stdout.Should().Contain("--kind");
    }

    [Fact]
    public void UnknownVerb_ListsAllCommandsOnStderr_ExitsWithUsageError()
    {
        var (exitCode, stdout, stderr) = RunHelp("bogus");

        exitCode.Should().Be(ExitCodes.Usage);
        stdout.Should().BeEmpty();
        foreach (var verb in new[] { "summary", "structure", "tags", "io", "metrics", "search", "diff", "export" })
            stderr.Should().MatchRegex($@"(?m)^\s+{verb}\s");
    }

    [Fact]
    public void InvalidOptionValue_ExitsWithUsageError()
    {
        var (exitCode, _, stderr) = RunHelp("summary", "project.L5X", "--format", "pdf");

        exitCode.Should().Be(ExitCodes.Usage);
        stderr.Should().Contain("format");
    }

    // ---- execution ----

    [Fact]
    public async Task MissingFile_ExitsOneWithErrorOnStderrOnly()
    {
        var command = new SummaryCommand { FilePath = Path.Combine(_tempDirectory, "missing.L5X") };

        var (exitCode, stdout, stderr) = await Run(command, new RockwellL5XParser());

        exitCode.Should().Be(ExitCodes.Error);
        stdout.Should().BeEmpty();
        stderr.Should().StartWith("plcinfo: error: ");
    }

    [Fact]
    public async Task Diff_UnreadableFile_ExitsTwo()
    {
        var command = new DiffCommand { File1 = "missing1.L5X", File2 = "missing2.L5X" };

        var (exitCode, _, _) = await Run(command, new RockwellL5XParser());

        exitCode.Should().Be(2);
    }

    [Fact]
    public async Task Search_UnknownKind_IsUsageError()
    {
        var command = new SearchCommand { FilePath = "x.test", Query = "x", Kind = "bogus" };

        var (exitCode, _, stderr) = await Run(command, new StubParser());

        exitCode.Should().Be(ExitCodes.Usage);
        stderr.Should().Contain("unknown --kind 'bogus'");
    }

    [Fact]
    public async Task Search_PluralKind_IsAccepted()
    {
        var command = new SearchCommand { FilePath = "x.test", Query = "x", Kind = "tags" };

        var (exitCode, _, _) = await Run(command, new StubParser());

        exitCode.Should().Be(ExitCodes.Success);
    }

    [Fact]
    public async Task Export_TextFormat_IsUsageError()
    {
        var command = new ExportCommand { FilePath = "x.test", Format = OutputFormat.Text };

        var (exitCode, _, _) = await Run(command, new StubParser());

        exitCode.Should().Be(ExitCodes.Usage);
    }

    [Fact]
    public async Task OutputOption_WritesFileAndNothingToStdout()
    {
        var outputPath = Path.Combine(_tempDirectory, "summary.json");
        var command = new SummaryCommand { FilePath = "x.test", Format = OutputFormat.Json, OutputPath = outputPath };

        var (exitCode, stdout, _) = await Run(command, new StubParser());

        exitCode.Should().Be(ExitCodes.Success);
        stdout.Should().BeEmpty();
        File.ReadAllText(outputPath).Should().Contain("\"vendor\"");
    }

    [Fact]
    public async Task ParserWarnings_GoToStderr_UnlessQuiet()
    {
        var loud = await Run(new SummaryCommand { FilePath = "x.test" }, new StubParser("Rung logic not decoded"));
        var quiet = await Run(new SummaryCommand { FilePath = "x.test", Quiet = true }, new StubParser("Rung logic not decoded"));

        loud.Stderr.Should().Contain("plcinfo: warning: x.test: Rung logic not decoded");
        loud.Stdout.Should().NotContain("warning");
        quiet.Stderr.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_ExitsWith130()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var (exitCode, _, _) = await CaptureConsoleAsync(() =>
            new SummaryCommand { FilePath = "x.test" }.Execute(Registry(new StubParser()), cancelled.Token));

        exitCode.Should().Be(ExitCodes.Interrupted);
    }

    // ---- helpers ----

    private static T Parse<T>(params string[] args) where T : class
    {
        var result = PlcInfo.Program.ParseArguments(args);
        result.Tag.Should().Be(ParserResultType.Parsed, string.Join(" ", args));
        return ((Parsed<object>)result).Value.Should().BeAssignableTo<T>().Subject;
    }

    private static (int ExitCode, string Stdout, string Stderr) RunHelp(params string[] args)
    {
        var result = PlcInfo.Program.ParseArguments(args);
        var errors = ((NotParsed<object>)result).Errors;
        return CaptureConsoleAsync(() => Task.FromResult(HelpFormatter.DisplayHelp(result, errors))).GetAwaiter().GetResult();
    }

    private static Task<(int ExitCode, string Stdout, string Stderr)> Run(CommandBase command, IPlcProjectParser parser) =>
        CaptureConsoleAsync(() => command.Execute(Registry(parser)));

    private static PlcParserRegistry Registry(IPlcProjectParser parser) => new(new[] { parser });

    private static async Task<(int ExitCode, string Stdout, string Stderr)> CaptureConsoleAsync(Func<Task<int>> action)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var exitCode = await action();
            return (exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    /// <summary>
    /// Parses any "*.test" path into a tiny project, optionally with parser warnings.
    /// </summary>
    private sealed class StubParser : IPlcProjectParser
    {
        private readonly string? _warning;
        private readonly Action<PlcProject>? _configure;

        public StubParser(string? warning = null, Action<PlcProject>? configure = null)
        {
            _warning = warning;
            _configure = configure;
        }

        public bool CanParse(string filePath) => filePath.EndsWith(".test", StringComparison.Ordinal);

        public Task<PlcProject> ParseAsync(string filePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var project = new PlcProject { Vendor = "Test", Format = "Test", FilePath = filePath };
            if (_warning != null)
                project.Metadata["Warnings"] = _warning;
            _configure?.Invoke(project);
            return Task.FromResult(project);
        }
    }
}
