using System.Text;
using System.Text.Json;
using PlcInfo.Commands;
using PlcInfo.Services;

namespace PlcInfo.Formatters;

/// <summary>
/// Renders analysis reports (cross-reference, lint) in every output format.
/// JSON and YAML use the same serializer settings as the other commands.
/// </summary>
public static class ReportFormatter
{
    public static string FormatXref(IReadOnlyList<XrefEntry> entries, OutputFormat format)
    {
        switch (format)
        {
            case OutputFormat.Json:
                return JsonSerializer.Serialize(entries, JsonOutputFormatter.Options);
            case OutputFormat.Yaml:
                return YamlOutputFormatter.Serializer.Serialize(entries);
        }

        var rows = entries.Select(e => new[]
        {
            e.Tag, e.Access, e.Operand, $"{e.Container}/{e.Routine}", e.Position, e.Instruction, e.Scope ?? "(undefined)"
        }).ToList();
        var header = new[] { "Tag", "Access", "Operand", "Routine", "Where", "Instruction", "Scope" };
        var summary = $"{entries.Count} reference(s) to {entries.Select(e => e.Tag).Distinct(StringComparer.OrdinalIgnoreCase).Count()} tag(s): " +
                      $"{entries.Count(e => e.Access == "Write")} write(s), {entries.Count(e => e.Access == "Read")} read(s).";

        return format == OutputFormat.Md
            ? MarkdownTable("Cross-Reference", summary, header, rows)
            : TextTable("Cross-Reference", summary, header, rows);
    }

    public static string FormatLint(IReadOnlyList<LintFinding> findings, OutputFormat format)
    {
        switch (format)
        {
            case OutputFormat.Json:
                return JsonSerializer.Serialize(findings, JsonOutputFormatter.Options);
            case OutputFormat.Yaml:
                return YamlOutputFormatter.Serializer.Serialize(findings);
        }

        var counts = Enum.GetValues<LintSeverity>()
            .Select(s => $"{findings.Count(f => f.Severity == s)} {s.ToString().ToLowerInvariant()}(s)");
        var summary = findings.Count == 0 ? "No findings." : string.Join(", ", counts) + ".";
        var header = new[] { "Severity", "Code", "Location", "Message" };
        var rows = findings.Select(f => new[] { f.Severity.ToString().ToLowerInvariant(), f.Code, f.Location, f.Message }).ToList();

        return format == OutputFormat.Md
            ? MarkdownTable("Lint", summary, header, rows)
            : TextTable("Lint", summary, header, rows);
    }

    private static string TextTable(string title, string summary, string[] header, List<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(title);
        sb.AppendLine(new string('=', title.Length));
        sb.AppendLine();
        sb.AppendLine(summary);
        if (rows.Count == 0)
            return sb.ToString();
        sb.AppendLine();

        // The last column (often a long message) is not padded.
        var widths = Enumerable.Range(0, header.Length - 1)
            .Select(c => Math.Min(40, rows.Select(r => r[c].Length).Append(header[c].Length).Max()))
            .ToArray();
        string Row(string[] cells) => string.Join("  ",
            cells.Select((cell, c) => c < widths.Length ? cell.PadRight(widths[c]) : cell)).TrimEnd();

        sb.AppendLine(Row(header));
        sb.AppendLine(Row(header.Select((h, c) => new string('-', c < widths.Length ? widths[c] : h.Length)).ToArray()));
        foreach (var row in rows)
            sb.AppendLine(Row(row));
        return sb.ToString();
    }

    private static string MarkdownTable(string title, string summary, string[] header, List<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {title}");
        sb.AppendLine();
        sb.AppendLine(summary);
        if (rows.Count == 0)
            return sb.ToString();
        sb.AppendLine();
        sb.AppendLine($"| {string.Join(" | ", header)} |");
        sb.AppendLine($"|{string.Join("|", header.Select(_ => "---"))}|");
        foreach (var row in rows)
            sb.AppendLine($"| {string.Join(" | ", row.Select(MarkdownOutputFormatter.Cell))} |");
        return sb.ToString();
    }
}
