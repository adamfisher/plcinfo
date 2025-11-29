using CommandLine;

namespace PlcInfo.Commands;

/// <summary>
/// Base options for commands that operate on a single file.
/// </summary>
public abstract class BaseFileCommand
{
    [Value(0, Required = true, HelpText = "PLC project file path", MetaName = "FILE")]
    public string FilePath { get; set; } = string.Empty;

    [Option('f', "format", Required = false, Default = OutputFormat.Text, HelpText = "Output format (text, json, yaml, md)")]
    public OutputFormat Format { get; set; } = OutputFormat.Text;
}

/// <summary>
/// Output format enumeration.
/// </summary>
public enum OutputFormat
{
    Text,
    Json,
    Yaml,
    Md
}