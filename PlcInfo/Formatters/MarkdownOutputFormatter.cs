using System.Text;
using Markdig;
using PlcInfo.Models;

namespace PlcInfo.Formatters;

/// <summary>
/// Markdown output formatter using Markdig.
/// </summary>
public class MarkdownOutputFormatter : IOutputFormatter
{
    /// <inheritdoc/>
    public string FormatSummary(PlcProject project)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# PLC Project Summary");
        sb.AppendLine();
        sb.AppendLine("## Project Information");
        sb.AppendLine();
        sb.AppendLine($"- **Vendor**: {project.Vendor}");
        sb.AppendLine($"- **Format**: {project.Format}");
        sb.AppendLine($"- **File**: `{project.FilePath}`");

        if (project.LastModified.HasValue)
        {
            sb.AppendLine($"- **Last Modified**: {project.LastModified:yyyy-MM-dd HH:mm:ss}");
        }
        sb.AppendLine();

        if (project.Controller != null)
        {
            sb.AppendLine("## Controller");
            sb.AppendLine();
            sb.AppendLine($"- **Name**: {project.Controller.Name}");
            sb.AppendLine($"- **Type**: {project.Controller.Type}");

            if (!string.IsNullOrEmpty(project.Controller.CpuType))
            {
                sb.AppendLine($"- **CPU**: {project.Controller.CpuType}");
            }
            if (!string.IsNullOrEmpty(project.Controller.FirmwareRevision))
            {
                sb.AppendLine($"- **Firmware**: {project.Controller.FirmwareRevision}");
            }
            sb.AppendLine();
        }

        if (project.Programs.Any())
        {
            sb.AppendLine("## Programs");
            sb.AppendLine();
            sb.AppendLine($"Total Programs: {project.Programs.Count}");
            sb.AppendLine($"Total Routines: {project.Programs.Sum(p => p.Routines.Count)}");

            var rungs = project.Programs.SelectMany(p => p.Routines).Sum(r => r.RungCount);
            if (rungs > 0)
            {
                sb.AppendLine($"Total Rungs: {rungs}");
            }
            sb.AppendLine();
        }

        if (project.Tasks.Any())
        {
            sb.AppendLine("## Tasks");
            sb.AppendLine();
            sb.AppendLine($"Total Tasks: {project.Tasks.Count}");
            sb.AppendLine();
        }

        if (project.Blocks.Any())
        {
            sb.AppendLine("## Blocks");
            sb.AppendLine();
            sb.AppendLine($"Total Blocks: {project.Blocks.Count}");

            var networks = project.Blocks.Sum(b => b.NetworkCount);
            if (networks > 0)
            {
                sb.AppendLine($"Total Networks: {networks}");
            }
            sb.AppendLine();
        }

        if (project.Tags.Any())
        {
            sb.AppendLine("## Tags");
            sb.AppendLine();
            sb.AppendLine($"Total Tags: {project.Tags.Count}");
            sb.AppendLine();
        }

        if (project.IoModules.Any())
        {
            sb.AppendLine("## I/O Modules");
            sb.AppendLine();
            sb.AppendLine($"Total I/O Modules: {project.IoModules.Count}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <inheritdoc/>
    public string FormatStructure(PlcProject project)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Project Structure");
        sb.AppendLine();

        if (project.Controller != null)
        {
            sb.AppendLine($"**Controller:** {project.Controller.Name} ({project.Controller.Type})");
            sb.AppendLine();
        }

        if (project.Tasks.Any())
        {
            sb.AppendLine("## Tasks");
            sb.AppendLine();
            sb.AppendLine("| Priority | Name | Type | Rate | Programs |");
            sb.AppendLine("|----------|------|------|------|----------|");

            foreach (var task in project.Tasks.OrderBy(t => t.Priority))
            {
                var rate = task.Rate.HasValue ? $"{task.Rate}ms" : "—";
                var programs = string.Join(", ", task.ProgramNames);
                sb.AppendLine($"| {task.Priority} | {Cell(task.Name)} | {Cell(task.Type)} | {rate} | {Cell(programs)} |");
            }
            sb.AppendLine();
        }

        if (project.Programs.Any())
        {
            sb.AppendLine("## Programs");
            sb.AppendLine();

            foreach (var program in project.Programs.OrderBy(p => p.Name))
            {
                sb.AppendLine($"### {program.Name}");
                sb.AppendLine();
                sb.AppendLine($"- **Type**: {program.Type}");
                sb.AppendLine($"- **Language**: {program.Language}");

                if (!string.IsNullOrEmpty(program.Description))
                {
                    sb.AppendLine($"- **Description**: {program.Description}");
                }

                if (program.Routines.Any())
                {
                    sb.AppendLine();
                    sb.AppendLine("#### Routines");
                    sb.AppendLine();

                    foreach (var routine in program.Routines)
                    {
                        sb.AppendLine($"- **{routine.Name}**");
                        sb.AppendLine($"  - Type: {routine.Type}");
                        sb.AppendLine($"  - Language: {routine.Language}");
                        if (routine.RungCount > 0)
                            sb.AppendLine($"  - Rungs: {routine.RungCount}");
                        if (routine.NetworkCount > 0)
                            sb.AppendLine($"  - Networks: {routine.NetworkCount}");
                    }
                }
                sb.AppendLine();
            }
        }

        if (project.Blocks.Any())
        {
            sb.AppendLine("## Blocks (Siemens)");
            sb.AppendLine();
            sb.AppendLine("| Type | Name | Number | Language | Networks | Title |");
            sb.AppendLine("|------|------|--------|----------|----------|-------|");

            foreach (var block in project.Blocks.OrderBy(b => b.Type).ThenBy(b => b.Number))
            {
                var title = !string.IsNullOrEmpty(block.Title) ? block.Title : "—";
                var networks = block.NetworkCount > 0 ? block.NetworkCount.ToString() : "—";
                sb.AppendLine($"| {Cell(block.Type)} | {Cell(block.Name)} | {block.Number} | {Cell(block.Language)} | {networks} | {Cell(title)} |");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <inheritdoc/>
    public string FormatTags(PlcProject project, TagFilter? filter = null)
    {
        var tags = FilterTags(TagFilter.AllTags(project), filter).ToList();

        var sb = new StringBuilder();

        sb.AppendLine("# Tags");
        sb.AppendLine();

        if (!tags.Any())
        {
            sb.AppendLine("*No tags found matching the filter criteria.*");
            return sb.ToString();
        }

        sb.AppendLine("| Name | Data Type | Scope | Description | Address |");
        sb.AppendLine("|------|-----------|-------|-------------|---------|");

        foreach (var tag in tags.OrderBy(t => t.Name))
        {
            var address = tag.Address ?? "-";
            var description = tag.Description ?? "-";

            sb.AppendLine($"| {Cell(tag.Name)} | {Cell(tag.DataType)} | {Cell(tag.Scope)} | {Cell(description)} | {Cell(address)} |");
        }

        return sb.ToString();
    }

    /// <inheritdoc/>
    public string FormatIo(PlcProject project)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# I/O Modules");
        sb.AppendLine();

        if (!project.IoModules.Any())
        {
            sb.AppendLine("*No I/O modules found.*");
            return sb.ToString();
        }

        foreach (var module in project.IoModules.OrderBy(m => m.Name))
        {
            sb.AppendLine($"## {module.Name}");
            sb.AppendLine();
            sb.AppendLine($"- **Type**: {module.Type}");

            if (!string.IsNullOrEmpty(module.CatalogNumber))
                sb.AppendLine($"- **Catalog**: {module.CatalogNumber}");

            if (!string.IsNullOrEmpty(module.Vendor))
                sb.AppendLine($"- **Vendor**: {module.Vendor}");

            if (module.ChannelCount > 0)
                sb.AppendLine($"- **Channels**: {module.ChannelCount}");

            if (!string.IsNullOrEmpty(module.Location))
                sb.AppendLine($"- **Location**: {module.Location}");

            if (!string.IsNullOrEmpty(module.CommFormat))
                sb.AppendLine($"- **Comm Format**: {module.CommFormat}");

            if (!string.IsNullOrEmpty(module.ParentModule))
                sb.AppendLine($"- **Parent Module**: {module.ParentModule}");

            sb.AppendLine();
        }

        var allIoTags = project.Tags.Where(t => t.IsIoTag).ToList();
        if (allIoTags.Any())
        {
            sb.AppendLine("## I/O Tag Summary");
            sb.AppendLine();
            sb.AppendLine("| Type | Count |");
            sb.AppendLine("|------|-------|");
            sb.AppendLine($"| Total I/O Tags | {allIoTags.Count} |");

            var inputTags = allIoTags.Count(t =>
                t.Address?.StartsWith("I", StringComparison.OrdinalIgnoreCase) == true ||
                t.Address?.StartsWith("%I", StringComparison.OrdinalIgnoreCase) == true);

            var outputTags = allIoTags.Count(t =>
                t.Address?.StartsWith("Q", StringComparison.OrdinalIgnoreCase) == true ||
                t.Address?.StartsWith("%Q", StringComparison.OrdinalIgnoreCase) == true);

            if (inputTags > 0)
                sb.AppendLine($"| Input Tags | {inputTags} |");
            if (outputTags > 0)
                sb.AppendLine($"| Output Tags | {outputTags} |");
        }

        return sb.ToString();
    }

    /// <inheritdoc/>
    public string FormatMetrics(MetricsSummary metrics)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Project Metrics");
        sb.AppendLine();

        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"- **Total Programs**: {metrics.TotalPrograms}");
        sb.AppendLine($"- **Total Routines**: {metrics.TotalRoutines}");
        sb.AppendLine($"- **Total Tags**: {metrics.TotalTags}");

        if (metrics.GlobalTags > 0)
            sb.AppendLine($"- **Global Tags**: {metrics.GlobalTags}");
        if (metrics.LocalTags > 0)
            sb.AppendLine($"- **Local Tags**: {metrics.LocalTags}");
        if (metrics.IoTags > 0)
            sb.AppendLine($"- **I/O Tags**: {metrics.IoTags}");

        if (metrics.TotalRungs > 0)
            sb.AppendLine($"- **Total Rungs**: {metrics.TotalRungs}");
        if (metrics.TotalBlocks > 0)
            sb.AppendLine($"- **Total Blocks**: {metrics.TotalBlocks}");
        if (metrics.TotalNetworks > 0)
            sb.AppendLine($"- **Total Networks**: {metrics.TotalNetworks}");
        if (metrics.TotalInstructions > 0)
            sb.AppendLine($"- **Total Instructions**: {metrics.TotalInstructions}");

        if (metrics.TotalIoModules > 0)
            sb.AppendLine($"- **Total I/O Modules**: {metrics.TotalIoModules}");
        if (metrics.InputModules > 0)
            sb.AppendLine($"- **Input Modules**: {metrics.InputModules}");
        if (metrics.OutputModules > 0)
            sb.AppendLine($"- **Output Modules**: {metrics.OutputModules}");

        if (metrics.TotalTasks > 0)
            sb.AppendLine($"- **Total Tasks**: {metrics.TotalTasks}");

        if (metrics.ComplexityScore > 0)
        {
            sb.AppendLine($"- **Complexity Score**: {metrics.ComplexityScore:F1}");
        }

        sb.AppendLine();

        if (metrics.LanguageDistribution.Any())
        {
            sb.AppendLine("## Language Distribution");
            sb.AppendLine();
            sb.AppendLine("| Language | Count |");
            sb.AppendLine("|----------|-------|");

            foreach (var lang in metrics.LanguageDistribution.OrderByDescending(kvp => kvp.Value))
            {
                sb.AppendLine($"| {Cell(lang.Key)} | {lang.Value} |");
            }
            sb.AppendLine();
        }

        if (metrics.DataTypeDistribution.Any())
        {
            sb.AppendLine("## Data Type Distribution");
            sb.AppendLine();
            sb.AppendLine("| Data Type | Count |");
            sb.AppendLine("|-----------|-------|");

            foreach (var type in metrics.DataTypeDistribution.OrderByDescending(kvp => kvp.Value))
            {
                sb.AppendLine($"| {Cell(type.Key)} | {type.Value} |");
            }
            sb.AppendLine();
        }

        if (metrics.TotalMemoryBytes.HasValue)
        {
            sb.AppendLine();
            sb.AppendLine($"**Estimated Memory Usage:** {FormatBytes(metrics.TotalMemoryBytes.Value)}");
        }

        return sb.ToString();
    }

    /// <inheritdoc/>
    public string FormatSearchResults(IEnumerable<SearchResult> results)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Search Results");
        sb.AppendLine();

        var resultList = results.ToList();
        if (!resultList.Any())
        {
            sb.AppendLine("*No results found.*");
            return sb.ToString();
        }

        sb.AppendLine($"Found {resultList.Count} match(es)");
        sb.AppendLine();

        foreach (var typeGroup in resultList.GroupBy(r => r.Type).OrderBy(g => g.Key))
        {
            sb.AppendLine($"## {typeGroup.Key} Matches");
            sb.AppendLine();

            foreach (var result in typeGroup)
            {
                sb.AppendLine($"### {result.Name}");
                sb.AppendLine();
                sb.AppendLine($"- **Location**: {result.Location}");

                if (!string.IsNullOrEmpty(result.Context))
                {
                    sb.AppendLine($"- **Context**: {result.Context}");
                }

                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <inheritdoc/>
    public string FormatDiff(Services.DiffResult result)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Diff Results");
        sb.AppendLine();
        sb.AppendLine(result.Summary);
        sb.AppendLine();

        if (!result.Details.Any())
        {
            sb.AppendLine("*No differences found.*");
            return sb.ToString();
        }

        foreach (var detail in result.Details)
        {
            // Include where and what changed, not just the message.
            var line = $"- **{detail.Type}** `{detail.Location}`: {detail.Message}";
            if (detail.Category == Services.PlcDiffService.Categories.Logic)
            {
                // Logic changes can be long or multi-line: show them as a diff block.
                sb.AppendLine(line);
                if (detail.Value1 != null || detail.Value2 != null)
                {
                    sb.AppendLine();
                    sb.AppendLine("  ```diff");
                    foreach (var old in SplitLines(detail.Value1))
                        sb.AppendLine($"  - {old}");
                    foreach (var @new in SplitLines(detail.Value2))
                        sb.AppendLine($"  + {@new}");
                    sb.AppendLine("  ```");
                    sb.AppendLine();
                }
                continue;
            }
            if (detail.Value1 != null || detail.Value2 != null)
                line += $" (`{detail.Value1 ?? "(none)"}` → `{detail.Value2 ?? "(none)"}`)";
            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    private static IEnumerable<TagInfo> FilterTags(IReadOnlyList<TagInfo> tags, TagFilter? filter)
    {
        if (filter == null)
            return tags;

        var filtered = tags.AsEnumerable();

        if (!string.IsNullOrEmpty(filter.Scope))
        {
            filtered = filtered.Where(t => t.Scope.Equals(filter.Scope, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(filter.Type))
        {
            filtered = filtered.Where(t => t.DataType.Contains(filter.Type, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.IoOnly)
        {
            filtered = filtered.Where(t => t.IsIoTag);
        }

        if (!string.IsNullOrEmpty(filter.NameContains))
        {
            filtered = filtered.Where(t => t.Name.Contains(filter.NameContains, StringComparison.OrdinalIgnoreCase));
        }

        return filtered;
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    /// <summary>
    /// Escapes a value for a Markdown table cell: '|' would start a new column and
    /// a newline would end the row.
    /// </summary>
    internal static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Replace("|", "\\|").Replace("\r\n", "<br>").Replace("\n", "<br>").Replace("\r", "<br>");
    }

    private static IEnumerable<string> SplitLines(string? value) =>
        string.IsNullOrEmpty(value) ? Enumerable.Empty<string>() : value.Replace("\r\n", "\n").Split('\n');
}
