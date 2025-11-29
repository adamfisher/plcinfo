using System.Text;
using PlcInfo.Models;

namespace PlcInfo.Formatters;

/// <summary>
/// Text output formatter.
/// </summary>
public class TextOutputFormatter : IOutputFormatter
{
    /// <inheritdoc/>
    public string FormatSummary(PlcProject project)
    {
        var sb = new StringBuilder();

        sb.AppendLine("PLC Project Summary");
        sb.AppendLine("===================");
        sb.AppendLine();

        sb.AppendLine($"File: {project.FilePath}");
        sb.AppendLine($"Vendor: {project.Vendor}");
        sb.AppendLine($"Format: {project.Format}");
        if (project.LastModified.HasValue)
        {
            sb.AppendLine($"Last Modified: {project.LastModified:yyyy-MM-dd HH:mm:ss}");
        }
        sb.AppendLine();

        if (project.Controller != null)
        {
            sb.AppendLine("Controller Information:");
            sb.AppendLine($"  Name: {project.Controller.Name}");
            sb.AppendLine($"  Type: {project.Controller.Type}");
            if (!string.IsNullOrEmpty(project.Controller.CpuType))
            {
                sb.AppendLine($"  CPU: {project.Controller.CpuType}");
            }
            if (!string.IsNullOrEmpty(project.Controller.FirmwareRevision))
            {
                sb.AppendLine($"  Firmware: {project.Controller.FirmwareRevision}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Statistics:");
        sb.AppendLine($"  Tasks: {project.Tasks.Count}");
        sb.AppendLine($"  Programs: {project.Programs.Count}");
        sb.AppendLine($"  Blocks: {project.Blocks.Count}");
        sb.AppendLine($"  Routines: {project.Programs.Sum(p => p.Routines.Count)}");
        sb.AppendLine($"  Tags: {project.Tags.Count}");
        sb.AppendLine($"  I/O Modules: {project.IoModules.Count}");

        var rungs = project.Programs.SelectMany(p => p.Routines).Sum(r => r.RungCount);
        if (rungs > 0)
        {
            sb.AppendLine($"  Total Rungs: {rungs}");
        }

        var networks = project.Blocks.Sum(b => b.NetworkCount);
        if (networks > 0)
        {
            sb.AppendLine($"  Total Networks: {networks}");
        }

        return sb.ToString();
    }

    /// <inheritdoc/>
    public string FormatStructure(PlcProject project)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Project Structure");
        sb.AppendLine("=================");
        sb.AppendLine();

        if (project.Controller != null)
        {
            sb.AppendLine($"Controller: {project.Controller.Name} ({project.Controller.Type})");
        }

        // Format task hierarchy
        if (project.Tasks.Any())
        {
            sb.AppendLine();
            sb.AppendLine("Tasks:");
            foreach (var task in project.Tasks.OrderBy(t => t.Priority))
            {
                sb.AppendLine($"  [{task.Priority}] {task.Name} ({task.Type})");
                if (task.Rate.HasValue)
                {
                    sb.AppendLine($"      Rate: {task.Rate}ms");
                }
                foreach (var programName in task.ProgramNames)
                {
                    sb.AppendLine($"      -> {programName}");
                }
            }
        }

        // Format programs
        if (project.Programs.Any())
        {
            sb.AppendLine();
            sb.AppendLine("Programs:");
            foreach (var program in project.Programs.OrderBy(p => p.Name))
            {
                sb.AppendLine($"  {program.Name} ({program.Type}) [{program.Language}]");
                foreach (var routine in program.Routines)
                {
                    var details = new List<string>();
                    if (routine.RungCount > 0)
                        details.Add($"{routine.RungCount} rungs");
                    if (routine.NetworkCount > 0)
                        details.Add($"{routine.NetworkCount} networks");

                    var detailStr = details.Any() ? $" ({string.Join(", ", details)})" : "";
                    sb.AppendLine($"    - {routine.Name} [{routine.Language}]{detailStr}");
                }
            }
        }

        // Format blocks (Siemens)
        if (project.Blocks.Any())
        {
            sb.AppendLine();
            sb.AppendLine("Blocks:");
            foreach (var blockGroup in project.Blocks.GroupBy(b => b.Type).OrderBy(g => g.Key))
            {
                sb.AppendLine($"  {blockGroup.Key}:");
                foreach (var block in blockGroup.OrderBy(b => b.Number))
                {
                    var details = new List<string> { block.Language };
                    if (block.NetworkCount > 0)
                        details.Add($"{block.NetworkCount} networks");

                    sb.AppendLine($"    {block.Name} ({string.Join(", ", details)})");
                    if (!string.IsNullOrEmpty(block.Title))
                    {
                        sb.AppendLine($"      \"{block.Title}\"");
                    }
                }
            }
        }

        return sb.ToString();
    }

    /// <inheritdoc/>
    public string FormatTags(PlcProject project, TagFilter? filter = null)
    {
        var tags = FilterTags(project.Tags, filter);

        var sb = new StringBuilder();

        sb.AppendLine("Tags");
        sb.AppendLine("====");
        sb.AppendLine();

        if (!tags.Any())
        {
            sb.AppendLine("No tags found matching the filter criteria.");
            return sb.ToString();
        }

        sb.AppendLine($"Total: {tags.Count()} tags");
        sb.AppendLine();

        // Group by scope
        foreach (var scopeGroup in tags.GroupBy(t => t.Scope).OrderBy(g => g.Key))
        {
            sb.AppendLine($"Scope: {scopeGroup.Key}");
            sb.AppendLine(new string('-', 50));

            foreach (var tag in scopeGroup.OrderBy(t => t.Name))
            {
                sb.Append($"  {tag.Name,-30} {tag.DataType,-15}");

                if (tag.IsIoTag)
                    sb.Append(" [I/O]");

                if (!string.IsNullOrEmpty(tag.Address))
                    sb.Append($" @ {tag.Address}");

                sb.AppendLine();

                if (!string.IsNullOrEmpty(tag.Description))
                {
                    sb.AppendLine($"    // {tag.Description}");
                }

                if (!string.IsNullOrEmpty(tag.InitialValue))
                {
                    sb.AppendLine($"    := {tag.InitialValue}");
                }
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <inheritdoc/>
    public string FormatIo(PlcProject project)
    {
        var sb = new StringBuilder();

        sb.AppendLine("I/O Configuration");
        sb.AppendLine("=================");
        sb.AppendLine();

        if (!project.IoModules.Any())
        {
            sb.AppendLine("No I/O modules found.");
            return sb.ToString();
        }

        sb.AppendLine($"Total Modules: {project.IoModules.Count}");
        sb.AppendLine();

        // Group by type
        foreach (var typeGroup in project.IoModules.GroupBy(m => m.Type).OrderBy(g => g.Key))
        {
            sb.AppendLine($"{typeGroup.Key}:");
            sb.AppendLine(new string('-', 50));

            foreach (var module in typeGroup.OrderBy(m => m.Name))
            {
                sb.AppendLine($"  {module.Name}");

                if (!string.IsNullOrEmpty(module.CatalogNumber))
                    sb.AppendLine($"    Catalog: {module.CatalogNumber}");

                if (!string.IsNullOrEmpty(module.Location))
                    sb.AppendLine($"    Location: {module.Location}");

                if (module.ChannelCount > 0)
                    sb.AppendLine($"    Channels: {module.ChannelCount}");

                // List associated tags
                var ioTags = project.Tags.Where(t =>
                    t.IsIoTag && (t.IoModule == module.Name || module.AssociatedTags.Contains(t.Name))).ToList();

                if (ioTags.Any())
                {
                    sb.AppendLine("    Associated Tags:");
                    foreach (var tag in ioTags.Take(5))
                    {
                        sb.AppendLine($"      - {tag.Name} ({tag.DataType})");
                    }
                    if (ioTags.Count > 5)
                    {
                        sb.AppendLine($"      ... and {ioTags.Count - 5} more");
                    }
                }
            }

            sb.AppendLine();
        }

        // Summary of I/O tags
        var allIoTags = project.Tags.Where(t => t.IsIoTag).ToList();
        if (allIoTags.Any())
        {
            sb.AppendLine("I/O Tag Summary:");
            sb.AppendLine($"  Total I/O Tags: {allIoTags.Count}");

            var inputTags = allIoTags.Where(t =>
                t.Address?.StartsWith("I", StringComparison.OrdinalIgnoreCase) == true ||
                t.Address?.StartsWith("%I", StringComparison.OrdinalIgnoreCase) == true).Count();

            var outputTags = allIoTags.Where(t =>
                t.Address?.StartsWith("Q", StringComparison.OrdinalIgnoreCase) == true ||
                t.Address?.StartsWith("%Q", StringComparison.OrdinalIgnoreCase) == true).Count();

            if (inputTags > 0)
                sb.AppendLine($"  Input Tags: {inputTags}");
            if (outputTags > 0)
                sb.AppendLine($"  Output Tags: {outputTags}");
        }

        return sb.ToString();
    }

    /// <inheritdoc/>
    public string FormatMetrics(MetricsSummary metrics)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Project Metrics");
        sb.AppendLine("===============");
        sb.AppendLine();

        sb.AppendLine("Program Metrics:");
        sb.AppendLine($"  Total Programs: {metrics.TotalPrograms}");
        sb.AppendLine($"  Total Routines: {metrics.TotalRoutines}");
        if (metrics.TotalRungs > 0)
            sb.AppendLine($"  Total Rungs: {metrics.TotalRungs}");
        if (metrics.TotalBlocks > 0)
            sb.AppendLine($"  Total Blocks: {metrics.TotalBlocks}");
        if (metrics.TotalNetworks > 0)
            sb.AppendLine($"  Total Networks: {metrics.TotalNetworks}");
        if (metrics.TotalInstructions > 0)
            sb.AppendLine($"  Total Instructions: {metrics.TotalInstructions}");

        sb.AppendLine();
        sb.AppendLine("Tag Metrics:");
        sb.AppendLine($"  Total Tags: {metrics.TotalTags}");
        sb.AppendLine($"  Global Tags: {metrics.GlobalTags}");
        sb.AppendLine($"  Local Tags: {metrics.LocalTags}");
        sb.AppendLine($"  I/O Tags: {metrics.IoTags}");

        sb.AppendLine();
        sb.AppendLine("I/O Metrics:");
        sb.AppendLine($"  Total I/O Modules: {metrics.TotalIoModules}");
        sb.AppendLine($"  Input Modules: {metrics.InputModules}");
        sb.AppendLine($"  Output Modules: {metrics.OutputModules}");

        sb.AppendLine();
        sb.AppendLine($"Total Tasks: {metrics.TotalTasks}");

        if (metrics.ComplexityScore > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Complexity Score: {metrics.ComplexityScore:F1}/100");
        }

        if (metrics.LanguageDistribution.Any())
        {
            sb.AppendLine();
            sb.AppendLine("Language Distribution:");
            foreach (var lang in metrics.LanguageDistribution.OrderByDescending(kvp => kvp.Value))
            {
                sb.AppendLine($"  {lang.Key}: {lang.Value}");
            }
        }

        if (metrics.DataTypeDistribution.Any())
        {
            sb.AppendLine();
            sb.AppendLine("Data Type Distribution (Top 10):");
            foreach (var type in metrics.DataTypeDistribution.OrderByDescending(kvp => kvp.Value).Take(10))
            {
                sb.AppendLine($"  {type.Key}: {type.Value}");
            }
        }

        if (metrics.TotalMemoryBytes.HasValue)
        {
            sb.AppendLine();
            sb.AppendLine($"Estimated Memory Usage: {FormatBytes(metrics.TotalMemoryBytes.Value)}");
        }

        return sb.ToString();
    }

    /// <inheritdoc/>
    public string FormatSearchResults(IEnumerable<SearchResult> results)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Search Results");
        sb.AppendLine("==============");
        sb.AppendLine();

        var resultList = results.ToList();
        if (!resultList.Any())
        {
            sb.AppendLine("No results found.");
            return sb.ToString();
        }

        sb.AppendLine($"Found {resultList.Count} results:");
        sb.AppendLine();

        // Group by type
        foreach (var typeGroup in resultList.GroupBy(r => r.Type).OrderBy(g => g.Key))
        {
            sb.AppendLine($"{typeGroup.Key}s:");
            foreach (var result in typeGroup)
            {
                sb.AppendLine($"  {result.Name}");
                sb.AppendLine($"    Location: {result.Location}");
                if (!string.IsNullOrEmpty(result.Context))
                {
                    sb.AppendLine($"    Context: {result.Context}");
                }
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <inheritdoc/>
    public string FormatDiff(Services.DiffResult result)
    {
        var sb = new StringBuilder();

        sb.AppendLine("Project Diff");
        sb.AppendLine("============");
        sb.AppendLine();

        sb.AppendLine($"File 1: {result.File1}");
        sb.AppendLine($"File 2: {result.File2}");
        sb.AppendLine();

        sb.AppendLine($"Summary: {result.Summary}");
        sb.AppendLine();

        if (!result.Details.Any())
        {
            sb.AppendLine("No differences found.");
            return sb.ToString();
        }

        // Group by type
        var added = result.Details.Where(d => d.Type == "Added").ToList();
        var removed = result.Details.Where(d => d.Type == "Removed").ToList();
        var modified = result.Details.Where(d => d.Type == "Modified").ToList();
        var info = result.Details.Where(d => d.Type == "Info").ToList();

        if (added.Any())
        {
            sb.AppendLine("Added:");
            sb.AppendLine(new string('-', 50));
            foreach (var detail in added)
            {
                sb.AppendLine($"  + {detail.Location}");
                sb.AppendLine($"      {detail.Message}");
                if (!string.IsNullOrEmpty(detail.Value2))
                {
                    sb.AppendLine($"      Value: {detail.Value2}");
                }
                sb.AppendLine();
            }
        }

        if (removed.Any())
        {
            sb.AppendLine("Removed:");
            sb.AppendLine(new string('-', 50));
            foreach (var detail in removed)
            {
                sb.AppendLine($"  - {detail.Location}");
                sb.AppendLine($"      {detail.Message}");
                if (!string.IsNullOrEmpty(detail.Value1))
                {
                    sb.AppendLine($"      Value: {detail.Value1}");
                }
                sb.AppendLine();
            }
        }

        if (modified.Any())
        {
            sb.AppendLine("Modified:");
            sb.AppendLine(new string('-', 50));
            foreach (var detail in modified)
            {
                sb.AppendLine($"  * {detail.Location}");
                sb.AppendLine($"      {detail.Message}");
                if (!string.IsNullOrEmpty(detail.Value1) && !string.IsNullOrEmpty(detail.Value2))
                {
                    sb.AppendLine($"      Old: {detail.Value1}");
                    sb.AppendLine($"      New: {detail.Value2}");
                }
                sb.AppendLine();
            }
        }

        if (info.Any())
        {
            sb.AppendLine("Informational:");
            sb.AppendLine(new string('-', 50));
            foreach (var detail in info)
            {
                sb.AppendLine($"  i {detail.Location}");
                sb.AppendLine($"      {detail.Message}");
                if (!string.IsNullOrEmpty(detail.Value1) || !string.IsNullOrEmpty(detail.Value2))
                {
                    if (!string.IsNullOrEmpty(detail.Value1))
                        sb.AppendLine($"      Old: {detail.Value1}");
                    if (!string.IsNullOrEmpty(detail.Value2))
                        sb.AppendLine($"      New: {detail.Value2}");
                }
                sb.AppendLine();
            }
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
}