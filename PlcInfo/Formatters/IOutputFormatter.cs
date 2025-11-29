using PlcInfo.Models;

namespace PlcInfo.Formatters;

/// <summary>
/// Interface for output formatters.
/// </summary>
public interface IOutputFormatter
{
    /// <summary>
    /// Formats a PLC project summary.
    /// </summary>
    string FormatSummary(PlcProject project);

    /// <summary>
    /// Formats the project structure.
    /// </summary>
    string FormatStructure(PlcProject project);

    /// <summary>
    /// Formats tags with optional filters.
    /// </summary>
    string FormatTags(PlcProject project, TagFilter? filter = null);

    /// <summary>
    /// Formats I/O modules and related tags.
    /// </summary>
    string FormatIo(PlcProject project);

    /// <summary>
    /// Formats project metrics.
    /// </summary>
    string FormatMetrics(MetricsSummary metrics);

    /// <summary>
    /// Formats search results.
    /// </summary>
    string FormatSearchResults(IEnumerable<SearchResult> results);

    /// <summary>
    /// Formats diff results.
    /// </summary>
    string FormatDiff(Services.DiffResult result);
}

/// <summary>
/// Tag filter options.
/// </summary>
public class TagFilter
{
    /// <summary>
    /// Filter by scope (e.g., Global, Local, Program name).
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// Filter by data type.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// Only show I/O tags.
    /// </summary>
    public bool IoOnly { get; set; }

    /// <summary>
    /// Filter by name containing this string.
    /// </summary>
    public string? NameContains { get; set; }
}

/// <summary>
/// Search result.
/// </summary>
public class SearchResult
{
    /// <summary>
    /// Type of item (Tag, Comment, Instruction).
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Item name or identifier.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Location in the project.
    /// </summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// Matching text or context.
    /// </summary>
    public string Context { get; set; } = string.Empty;
}