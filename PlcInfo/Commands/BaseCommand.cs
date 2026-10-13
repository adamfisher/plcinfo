using System.Text;
using CommandLine;
using PlcInfo.Formatters;
using PlcInfo.Models;
using PlcInfo.Parsing;

namespace PlcInfo.Commands;

/// <summary>
/// Process exit codes. Follows common conventions: 0 success, 1 runtime error,
/// 2 usage error, 130 interrupted (128 + SIGINT). <c>diff</c> follows diff(1):
/// 0 identical, 1 different, 2 trouble.
/// </summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int Error = 1;
    public const int Differences = 1;
    public const int Usage = 2;
    public const int Interrupted = 130;
}

/// <summary>
/// Thrown for invalid arguments that the argument parser cannot detect on its own
/// (e.g. an unknown option value). Results in exit code 2.
/// </summary>
public class UsageException : Exception
{
    public UsageException(string message) : base(message) { }
}

/// <summary>
/// Options and behaviour shared by every command.
/// </summary>
public abstract class CommandBase
{
    // No Default here: the parser would apply it even when -f is omitted, overriding
    // commands with a different default (export defaults to json).
    [Option('f', "format", Required = false, MetaValue = "FORMAT",
        HelpText = "Output format: text, json, yaml, md (markdown). Default: text (export: json).")]
    public OutputFormat Format { get; set; } = OutputFormat.Text;

    [Option('o', "output", Required = false, MetaValue = "FILE",
        HelpText = "Write output to FILE instead of standard output.")]
    public string? OutputPath { get; set; }

    [Option('q', "quiet", Required = false,
        HelpText = "Suppress warnings on standard error.")]
    public bool Quiet { get; set; }

    [Option("verbose", Required = false,
        HelpText = "Print additional diagnostics (including stack traces) on standard error.")]
    public bool Verbose { get; set; }

    /// <summary>
    /// Runs the command, mapping failures to exit codes and messages on standard error.
    /// Standard output only ever carries the command's result.
    /// </summary>
    public async Task<int> Execute(IPlcParserRegistry parserRegistry, CancellationToken cancellationToken = default)
    {
        try
        {
            return await RunAsync(parserRegistry, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine("plcinfo: interrupted");
            return ExitCodes.Interrupted;
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine($"plcinfo: {ex.Message}");
            Console.Error.WriteLine("Run 'plcinfo --help' for usage.");
            return ExitCodes.Usage;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"plcinfo: error: {ex.Message}");
            if (Verbose)
                Console.Error.WriteLine(ex);
            return ErrorExitCode;
        }
    }

    /// <summary>
    /// Exit code for runtime errors. Overridden by commands whose exit code 1 means something else.
    /// </summary>
    protected virtual int ErrorExitCode => ExitCodes.Error;

    /// <summary>
    /// Command body.
    /// </summary>
    protected abstract Task<int> RunAsync(IPlcParserRegistry parserRegistry, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a parser for <paramref name="path"/>, parses it, and reports parser warnings.
    /// </summary>
    protected async Task<PlcProject> LoadProjectAsync(IPlcParserRegistry parserRegistry, string path, CancellationToken cancellationToken)
    {
        var parser = parserRegistry.ResolveForFile(path);
        var project = await parser.ParseAsync(path, cancellationToken);

        if (!Quiet && project.Metadata.TryGetValue("Warnings", out var warnings) && !string.IsNullOrWhiteSpace(warnings))
        {
            foreach (var warning in warnings.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                Console.Error.WriteLine($"plcinfo: warning: {Path.GetFileName(path)}: {warning}");
        }

        return project;
    }

    /// <summary>
    /// The formatter for <see cref="Format"/>.
    /// </summary>
    protected IOutputFormatter Formatter => FormatterHelper.GetFormatter(Format);

    /// <summary>
    /// Writes the command result to standard output, or to <see cref="OutputPath"/> when given.
    /// </summary>
    protected async Task WriteResultAsync(string content, CancellationToken cancellationToken)
    {
        if (!content.EndsWith('\n'))
            content += Environment.NewLine;

        if (string.IsNullOrEmpty(OutputPath) || OutputPath == "-")
        {
            await Console.Out.WriteAsync(content);
            await Console.Out.FlushAsync(cancellationToken);
            return;
        }

        await File.WriteAllTextAsync(OutputPath, content, new UTF8Encoding(false), cancellationToken);
        if (Verbose)
            Console.Error.WriteLine($"plcinfo: wrote {OutputPath}");
    }
}

/// <summary>
/// Base for commands that operate on a single project file.
/// </summary>
public abstract class BaseFileCommand : CommandBase
{
    [Value(0, Required = true, MetaName = "FILE", HelpText = "PLC project file or export directory.")]
    public string FilePath { get; set; } = string.Empty;
}

/// <summary>
/// Output format enumeration.
/// </summary>
public enum OutputFormat
{
    Text,
    Json,
    Yaml,
    Md,
    Markdown = Md
}
