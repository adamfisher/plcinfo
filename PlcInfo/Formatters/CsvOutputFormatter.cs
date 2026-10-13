using System.Globalization;
using System.Text;
using PlcInfo.Commands;
using PlcInfo.Models;
using PlcInfo.Services;

namespace PlcInfo.Formatters;

/// <summary>
/// RFC 4180 CSV output for tabular results (tags, I/O, search, diff, xref, lint).
/// Non-tabular results (summary, structure, metrics) are a usage error.
/// </summary>
public class CsvOutputFormatter : IOutputFormatter
{
    /// <inheritdoc/>
    public string FormatSummary(PlcProject project) => throw NotTabular("summary");

    /// <inheritdoc/>
    public string FormatStructure(PlcProject project) => throw NotTabular("structure");

    /// <inheritdoc/>
    public string FormatMetrics(MetricsSummary metrics) => throw NotTabular("metrics");

    /// <inheritdoc/>
    public string FormatTags(PlcProject project, TagFilter? filter = null)
    {
        var tags = TagFilter.AllTags(project).AsEnumerable();
        if (filter != null)
        {
            if (!string.IsNullOrEmpty(filter.Scope))
                tags = tags.Where(t => t.Scope.Equals(filter.Scope, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(filter.Type))
                tags = tags.Where(t => t.DataType.Contains(filter.Type, StringComparison.OrdinalIgnoreCase));
            if (filter.IoOnly)
                tags = tags.Where(t => t.IsIoTag);
            if (!string.IsNullOrEmpty(filter.NameContains))
                tags = tags.Where(t => t.Name.Contains(filter.NameContains, StringComparison.OrdinalIgnoreCase));
        }

        return Table(
            new[] { "Scope", "Name", "DataType", "Dimensions", "Usage", "Address", "AliasFor", "IsIo", "Description" },
            tags.Select(t => new[]
            {
                t.Scope, t.Name, t.DataType,
                t.ArrayDimensions is { Length: > 0 } d ? string.Join(",", d.Select(n => n.ToString(CultureInfo.InvariantCulture))) : "",
                t.Usage, t.Address, t.AliasFor, t.IsIoTag ? "true" : "false", t.Description
            }));
    }

    /// <inheritdoc/>
    public string FormatIo(PlcProject project)
    {
        return Table(
            new[] { "Name", "Type", "CatalogNumber", "Vendor", "Location", "ParentModule", "CommFormat", "ChannelCount" },
            project.IoModules.Select(m => new[]
            {
                m.Name, m.Type, m.CatalogNumber, m.Vendor, m.Location, m.ParentModule, m.CommFormat,
                m.ChannelCount.ToString(CultureInfo.InvariantCulture)
            }));
    }

    /// <inheritdoc/>
    public string FormatSearchResults(IEnumerable<SearchResult> results)
    {
        return Table(new[] { "Type", "Name", "Location", "Context" },
            results.Select(r => new[] { r.Type, r.Name, r.Location, r.Context }));
    }

    /// <inheritdoc/>
    public string FormatDiff(DiffResult result)
    {
        return Table(new[] { "Type", "Category", "Location", "Message", "OldValue", "NewValue" },
            result.Details.Select(d => new[] { d.Type, d.Category, d.Location, d.Message, d.Value1, d.Value2 }));
    }

    /// <summary>
    /// Cross-reference rows.
    /// </summary>
    public static string FormatXref(IEnumerable<XrefEntry> entries) =>
        Table(new[] { "Tag", "Access", "Operand", "Container", "Routine", "Position", "Instruction", "Scope" },
            entries.Select(e => new[] { e.Tag, e.Access, e.Operand, e.Container, e.Routine, e.Position, e.Instruction, e.Scope }));

    /// <summary>
    /// Lint finding rows.
    /// </summary>
    public static string FormatLint(IEnumerable<LintFinding> findings) =>
        Table(new[] { "Severity", "Code", "Location", "Message" },
            findings.Select(f => new[] { f.Severity.ToString().ToLowerInvariant(), f.Code, f.Location, f.Message }));

    /// <summary>
    /// Writes a header and rows; fields are quoted when they contain a comma, quote or line break.
    /// </summary>
    internal static string Table(IEnumerable<string> header, IEnumerable<IEnumerable<string?>> rows)
    {
        var sb = new StringBuilder();
        void Row(IEnumerable<string?> cells) => sb.Append(string.Join(",", cells.Select(Escape))).Append("\r\n");

        Row(header);
        foreach (var row in rows)
            Row(row);
        return sb.ToString();
    }

    internal static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        // Guard against spreadsheet formula injection from untrusted project text.
        if (value[0] is '=' or '+' or '-' or '@' && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            value = "'" + value;
        return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    private static UsageException NotTabular(string command) =>
        new($"{command}: csv output is only available for tabular commands (tags, io, search, diff, xref, lint).");
}
