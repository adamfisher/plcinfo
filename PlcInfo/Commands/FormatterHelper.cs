using PlcInfo.Formatters;

namespace PlcInfo.Commands;

/// <summary>
/// Helper class for output formatting.
/// </summary>
public static class FormatterHelper
{
    /// <summary>
    /// Gets the appropriate formatter based on the output format.
    /// </summary>
    public static IOutputFormatter GetFormatter(OutputFormat format)
    {
        return format switch
        {
            OutputFormat.Json => new JsonOutputFormatter(),
            OutputFormat.Yaml => new YamlOutputFormatter(),
            OutputFormat.Md => new MarkdownOutputFormatter(),
            OutputFormat.Csv => new CsvOutputFormatter(),
            _ => new TextOutputFormatter()
        };
    }
}