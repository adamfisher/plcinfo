using PlcInfo.Commands;
using PlcInfo.Models;
using PlcInfo.Rendering;
using PlcInfo.Services;

namespace PlcInfo.Formatters;

/// <summary>
/// HTML output. A visual diff is available through <c>diff --format html</c>; the full
/// project report is the <c>report</c> command. Other commands reject html.
/// </summary>
public class HtmlOutputFormatter : IOutputFormatter
{
    /// <inheritdoc/>
    public string FormatDiff(DiffResult result) => HtmlReportRenderer.RenderDiff(result);

    /// <inheritdoc/>
    public string FormatSummary(PlcProject project) => throw NotSupported("summary");

    /// <inheritdoc/>
    public string FormatStructure(PlcProject project) => throw NotSupported("structure");

    /// <inheritdoc/>
    public string FormatTags(PlcProject project, TagFilter? filter = null) => throw NotSupported("tags");

    /// <inheritdoc/>
    public string FormatIo(PlcProject project) => throw NotSupported("io");

    /// <inheritdoc/>
    public string FormatMetrics(MetricsSummary metrics) => throw NotSupported("metrics");

    /// <inheritdoc/>
    public string FormatSearchResults(IEnumerable<SearchResult> results) => throw NotSupported("search");

    private static UsageException NotSupported(string command) =>
        new($"{command}: html output is available for 'report' and 'diff'. Use 'plcinfo report FILE' for an HTML project report.");
}
