using CommandLine;
using FluentAssertions;
using PlcInfo.Commands;
using PlcInfo.Parsing;
using Xunit.Categories;

namespace PlcInfo.Tests.Commands;

[UnitTest]
[Collection("Console")]
public class CommandLineTests
{
    [Theory]
    [InlineData("json", OutputFormat.Json)]
    [InlineData("Json", OutputFormat.Json)]
    [InlineData("md", OutputFormat.Md)]
    [InlineData("YAML", OutputFormat.Yaml)]
    public void FormatOption_IsCaseInsensitive(string value, OutputFormat expected)
    {
        // The README documents "--format json"; it used to be rejected.
        var result = PlcInfo.Program.ParseArguments(new[] { "summary", "project.L5X", "--format", value });

        result.Tag.Should().Be(ParserResultType.Parsed);
        ((SummaryCommand)((Parsed<object>)result).Value).Format.Should().Be(expected);
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public void ExportIndent_AcceptsExplicitValue(string value, bool expected)
    {
        var result = PlcInfo.Program.ParseArguments(new[] { "export", "project.L5X", "--indent", value });

        result.Tag.Should().Be(ParserResultType.Parsed);
        ((ExportCommand)((Parsed<object>)result).Value).Indent.Should().Be(expected);
    }

    [Fact]
    public void Help_WithUnknownVerb_ListsAllCommands()
    {
        var result = PlcInfo.Program.ParseArguments(new[] { "bogus" });
        var errors = ((NotParsed<object>)result).Errors;

        var (exitCode, stdout, stderr) = CaptureConsole(() => HelpFormatter.DisplayHelp(result, errors));

        exitCode.Should().Be(1);
        stdout.Should().BeEmpty();
        foreach (var verb in new[] { "summary", "structure", "tags", "io", "metrics", "search", "diff", "export" })
        {
            stderr.Should().MatchRegex($@"(?m)^\s+{verb}\s");
        }
    }

    [Fact]
    public void Help_ForVerb_ExitsZeroAndWritesToStdout()
    {
        var result = PlcInfo.Program.ParseArguments(new[] { "summary", "--help" });
        var errors = ((NotParsed<object>)result).Errors;

        var (exitCode, stdout, _) = CaptureConsole(() => HelpFormatter.DisplayHelp(result, errors));

        exitCode.Should().Be(0);
        stdout.Should().Contain("--format");
    }

    [Fact]
    public async Task Diff_UnreadableFile_ReturnsExitCode2()
    {
        var command = new DiffCommand { File1 = "missing1.L5X", File2 = "missing2.L5X" };
        var registry = new PlcParserRegistry(new IPlcProjectParser[] { new RockwellL5XParser() });

        var (exitCode, _, _) = await CaptureConsoleAsync(() => command.Execute(registry));

        exitCode.Should().Be(2);
    }

    [Fact]
    public async Task Search_UnknownKind_ReturnsErrorInsteadOfEmptyResults()
    {
        var command = new SearchCommand { FilePath = "project.L5X", SearchText = "x", Kind = "tags" };
        var registry = new PlcParserRegistry(new IPlcProjectParser[] { new RockwellL5XParser() });

        var (exitCode, _, stderr) = await CaptureConsoleAsync(() => command.Execute(registry));

        exitCode.Should().Be(1);
        stderr.Should().Contain("Unknown search kind 'tags'");
    }

    private static (int, string, string) CaptureConsole(Func<int> action) =>
        CaptureConsoleAsync(() => Task.FromResult(action())).GetAwaiter().GetResult();

    private static async Task<(int, string, string)> CaptureConsoleAsync(Func<Task<int>> action)
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
}
