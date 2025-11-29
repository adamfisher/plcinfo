using PlcInfo.Models;

namespace PlcInfo.Services;

/// <summary>
/// Service for comparing PLC projects.
/// </summary>
public class PlcDiffService
{
    /// <summary>
    /// Compares two PLC projects and returns detailed differences.
    /// </summary>
    public DiffResult Compare(PlcProject project1, PlcProject project2)
    {
        var details = new List<DiffDetail>();

        // Compare basic project properties
        CompareBasicProperties(project1, project2, details);

        // Compare controller information
        CompareController(project1.Controller, project2.Controller, details);

        // Compare tasks
        CompareTasks(project1.Tasks, project2.Tasks, details);

        // Compare programs
        ComparePrograms(project1.Programs, project2.Programs, details);

        // Compare blocks
        CompareBlocks(project1.Blocks, project2.Blocks, details);

        // Compare tags
        CompareTags(project1.Tags, project2.Tags, details);

        // Compare I/O modules
        CompareIoModules(project1.IoModules, project2.IoModules, details);

        // Compare metadata
        CompareMetadata(project1.Metadata, project2.Metadata, details);

        // Generate summary
        var hasDifferences = details.Any(d => d.Type != "Info");
        var summary = GenerateSummary(details);

        return new DiffResult
        {
            File1 = project1.FilePath,
            File2 = project2.FilePath,
            Summary = summary,
            HasDifferences = hasDifferences,
            Details = details
        };
    }

    private void CompareBasicProperties(PlcProject p1, PlcProject p2, List<DiffDetail> details)
    {
        if (p1.Vendor != p2.Vendor)
        {
            details.Add(new DiffDetail
            {
                Type = "Modified",
                Location = "Project.Vendor",
                Message = "Vendor changed",
                Value1 = p1.Vendor,
                Value2 = p2.Vendor
            });
        }

        if (p1.Format != p2.Format)
        {
            details.Add(new DiffDetail
            {
                Type = "Modified",
                Location = "Project.Format",
                Message = "Format changed",
                Value1 = p1.Format,
                Value2 = p2.Format
            });
        }

        if (p1.LastModified != p2.LastModified)
        {
            details.Add(new DiffDetail
            {
                Type = "Info",
                Location = "Project.LastModified",
                Message = "Last modified date changed",
                Value1 = p1.LastModified?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A",
                Value2 = p2.LastModified?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A"
            });
        }
    }

    private void CompareController(ControllerInfo? c1, ControllerInfo? c2, List<DiffDetail> details)
    {
        if (c1 == null && c2 == null) return;

        if (c1 == null)
        {
            details.Add(new DiffDetail
            {
                Type = "Added",
                Location = "Controller",
                Message = "Controller information added",
                Value2 = c2?.Name
            });
            return;
        }

        if (c2 == null)
        {
            details.Add(new DiffDetail
            {
                Type = "Removed",
                Location = "Controller",
                Message = "Controller information removed",
                Value1 = c1.Name
            });
            return;
        }

        if (c1.Name != c2.Name)
        {
            details.Add(new DiffDetail
            {
                Type = "Modified",
                Location = "Controller.Name",
                Message = "Controller name changed",
                Value1 = c1.Name,
                Value2 = c2.Name
            });
        }

        if (c1.Type != c2.Type)
        {
            details.Add(new DiffDetail
            {
                Type = "Modified",
                Location = "Controller.Type",
                Message = "Controller type changed",
                Value1 = c1.Type,
                Value2 = c2.Type
            });
        }

        if (c1.FirmwareRevision != c2.FirmwareRevision)
        {
            details.Add(new DiffDetail
            {
                Type = "Modified",
                Location = "Controller.FirmwareRevision",
                Message = "Firmware revision changed",
                Value1 = c1.FirmwareRevision,
                Value2 = c2.FirmwareRevision
            });
        }
    }

    private void CompareTasks(IReadOnlyList<TaskInfo> tasks1, IReadOnlyList<TaskInfo> tasks2, List<DiffDetail> details)
    {
        var names1 = tasks1.Select(t => t.Name).ToHashSet();
        var names2 = tasks2.Select(t => t.Name).ToHashSet();

        // Find added tasks
        foreach (var name in names2.Except(names1))
        {
            var task = tasks2.First(t => t.Name == name);
            details.Add(new DiffDetail
            {
                Type = "Added",
                Location = $"Tasks.{name}",
                Message = $"Task '{name}' added",
                Value2 = $"Type: {task.Type}, Priority: {task.Priority}"
            });
        }

        // Find removed tasks
        foreach (var name in names1.Except(names2))
        {
            var task = tasks1.First(t => t.Name == name);
            details.Add(new DiffDetail
            {
                Type = "Removed",
                Location = $"Tasks.{name}",
                Message = $"Task '{name}' removed",
                Value1 = $"Type: {task.Type}, Priority: {task.Priority}"
            });
        }

        // Find modified tasks
        foreach (var name in names1.Intersect(names2))
        {
            var task1 = tasks1.First(t => t.Name == name);
            var task2 = tasks2.First(t => t.Name == name);

            if (task1.Type != task2.Type)
            {
                details.Add(new DiffDetail
                {
                    Type = "Modified",
                    Location = $"Tasks.{name}.Type",
                    Message = $"Task '{name}' type changed",
                    Value1 = task1.Type,
                    Value2 = task2.Type
                });
            }

            if (task1.Priority != task2.Priority)
            {
                details.Add(new DiffDetail
                {
                    Type = "Modified",
                    Location = $"Tasks.{name}.Priority",
                    Message = $"Task '{name}' priority changed",
                    Value1 = task1.Priority.ToString(),
                    Value2 = task2.Priority.ToString()
                });
            }

            if (task1.WatchdogTimeout != task2.WatchdogTimeout)
            {
                details.Add(new DiffDetail
                {
                    Type = "Modified",
                    Location = $"Tasks.{name}.WatchdogTimeout",
                    Message = $"Task '{name}' watchdog timeout changed",
                    Value1 = task1.WatchdogTimeout?.ToString() ?? "N/A",
                    Value2 = task2.WatchdogTimeout?.ToString() ?? "N/A"
                });
            }
        }
    }

    private void ComparePrograms(IReadOnlyList<ProgramInfo> programs1, IReadOnlyList<ProgramInfo> programs2, List<DiffDetail> details)
    {
        var names1 = programs1.Select(p => p.Name).ToHashSet();
        var names2 = programs2.Select(p => p.Name).ToHashSet();

        // Find added programs
        foreach (var name in names2.Except(names1))
        {
            var program = programs2.First(p => p.Name == name);
            details.Add(new DiffDetail
            {
                Type = "Added",
                Location = $"Programs.{name}",
                Message = $"Program '{name}' added",
                Value2 = $"Type: {program.Type}, Language: {program.Language}, Routines: {program.Routines.Count}"
            });
        }

        // Find removed programs
        foreach (var name in names1.Except(names2))
        {
            var program = programs1.First(p => p.Name == name);
            details.Add(new DiffDetail
            {
                Type = "Removed",
                Location = $"Programs.{name}",
                Message = $"Program '{name}' removed",
                Value1 = $"Type: {program.Type}, Language: {program.Language}, Routines: {program.Routines.Count}"
            });
        }

        // Find modified programs
        foreach (var name in names1.Intersect(names2))
        {
            var program1 = programs1.First(p => p.Name == name);
            var program2 = programs2.First(p => p.Name == name);

            if (program1.Type != program2.Type)
            {
                details.Add(new DiffDetail
                {
                    Type = "Modified",
                    Location = $"Programs.{name}.Type",
                    Message = $"Program '{name}' type changed",
                    Value1 = program1.Type,
                    Value2 = program2.Type
                });
            }

            if (program1.Language != program2.Language)
            {
                details.Add(new DiffDetail
                {
                    Type = "Modified",
                    Location = $"Programs.{name}.Language",
                    Message = $"Program '{name}' language changed",
                    Value1 = program1.Language,
                    Value2 = program2.Language
                });
            }

            if (program1.Routines.Count != program2.Routines.Count)
            {
                details.Add(new DiffDetail
                {
                    Type = "Modified",
                    Location = $"Programs.{name}.Routines",
                    Message = $"Program '{name}' routine count changed",
                    Value1 = program1.Routines.Count.ToString(),
                    Value2 = program2.Routines.Count.ToString()
                });
            }

            if (program1.LocalTags.Count != program2.LocalTags.Count)
            {
                details.Add(new DiffDetail
                {
                    Type = "Modified",
                    Location = $"Programs.{name}.LocalTags",
                    Message = $"Program '{name}' local tag count changed",
                    Value1 = program1.LocalTags.Count.ToString(),
                    Value2 = program2.LocalTags.Count.ToString()
                });
            }
        }
    }

    private void CompareBlocks(IReadOnlyList<BlockInfo> blocks1, IReadOnlyList<BlockInfo> blocks2, List<DiffDetail> details)
    {
        var names1 = blocks1.Select(b => b.Name).ToHashSet();
        var names2 = blocks2.Select(b => b.Name).ToHashSet();

        // Find added blocks
        foreach (var name in names2.Except(names1))
        {
            var block = blocks2.First(b => b.Name == name);
            details.Add(new DiffDetail
            {
                Type = "Added",
                Location = $"Blocks.{name}",
                Message = $"Block '{name}' added",
                Value2 = $"Type: {block.Type}, Language: {block.Language}"
            });
        }

        // Find removed blocks
        foreach (var name in names1.Except(names2))
        {
            var block = blocks1.First(b => b.Name == name);
            details.Add(new DiffDetail
            {
                Type = "Removed",
                Location = $"Blocks.{name}",
                Message = $"Block '{name}' removed",
                Value1 = $"Type: {block.Type}, Language: {block.Language}"
            });
        }

        // Find modified blocks
        foreach (var name in names1.Intersect(names2))
        {
            var block1 = blocks1.First(b => b.Name == name);
            var block2 = blocks2.First(b => b.Name == name);

            if (block1.Type != block2.Type)
            {
                details.Add(new DiffDetail
                {
                    Type = "Modified",
                    Location = $"Blocks.{name}.Type",
                    Message = $"Block '{name}' type changed",
                    Value1 = block1.Type,
                    Value2 = block2.Type
                });
            }

            if (block1.Language != block2.Language)
            {
                details.Add(new DiffDetail
                {
                    Type = "Modified",
                    Location = $"Blocks.{name}.Language",
                    Message = $"Block '{name}' language changed",
                    Value1 = block1.Language,
                    Value2 = block2.Language
                });
            }
        }
    }

    private void CompareTags(IReadOnlyList<TagInfo> tags1, IReadOnlyList<TagInfo> tags2, List<DiffDetail> details)
    {
        var names1 = tags1.Select(t => t.Name).ToHashSet();
        var names2 = tags2.Select(t => t.Name).ToHashSet();

        // Find added tags
        foreach (var name in names2.Except(names1))
        {
            var tag = tags2.First(t => t.Name == name);
            details.Add(new DiffDetail
            {
                Type = "Added",
                Location = $"Tags.{name}",
                Message = $"Tag '{name}' added",
                Value2 = $"Type: {tag.DataType}, Scope: {tag.Scope}"
            });
        }

        // Find removed tags
        foreach (var name in names1.Except(names2))
        {
            var tag = tags1.First(t => t.Name == name);
            details.Add(new DiffDetail
            {
                Type = "Removed",
                Location = $"Tags.{name}",
                Message = $"Tag '{name}' removed",
                Value1 = $"Type: {tag.DataType}, Scope: {tag.Scope}"
            });
        }

        // Find modified tags
        foreach (var name in names1.Intersect(names2))
        {
            var tag1 = tags1.First(t => t.Name == name);
            var tag2 = tags2.First(t => t.Name == name);

            if (tag1.DataType != tag2.DataType)
            {
                details.Add(new DiffDetail
                {
                    Type = "Modified",
                    Location = $"Tags.{name}.DataType",
                    Message = $"Tag '{name}' data type changed",
                    Value1 = tag1.DataType,
                    Value2 = tag2.DataType
                });
            }

            if (tag1.Scope != tag2.Scope)
            {
                details.Add(new DiffDetail
                {
                    Type = "Modified",
                    Location = $"Tags.{name}.Scope",
                    Message = $"Tag '{name}' scope changed",
                    Value1 = tag1.Scope,
                    Value2 = tag2.Scope
                });
            }

            if (tag1.Usage != tag2.Usage)
            {
                details.Add(new DiffDetail
                {
                    Type = "Modified",
                    Location = $"Tags.{name}.Usage",
                    Message = $"Tag '{name}' usage changed",
                    Value1 = tag1.Usage ?? "N/A",
                    Value2 = tag2.Usage ?? "N/A"
                });
            }

            if (tag1.InitialValue != tag2.InitialValue)
            {
                details.Add(new DiffDetail
                {
                    Type = "Modified",
                    Location = $"Tags.{name}.InitialValue",
                    Message = $"Tag '{name}' initial value changed",
                    Value1 = tag1.InitialValue ?? "N/A",
                    Value2 = tag2.InitialValue ?? "N/A"
                });
            }

            if (tag1.Description != tag2.Description)
            {
                details.Add(new DiffDetail
                {
                    Type = "Info",
                    Location = $"Tags.{name}.Description",
                    Message = $"Tag '{name}' description changed",
                    Value1 = tag1.Description ?? "",
                    Value2 = tag2.Description ?? ""
                });
            }
        }
    }

    private void CompareIoModules(IReadOnlyList<IoModuleInfo> modules1, IReadOnlyList<IoModuleInfo> modules2, List<DiffDetail> details)
    {
        var names1 = modules1.Select(m => m.Name).ToHashSet();
        var names2 = modules2.Select(m => m.Name).ToHashSet();

        // Find added modules
        foreach (var name in names2.Except(names1))
        {
            var module = modules2.First(m => m.Name == name);
            details.Add(new DiffDetail
            {
                Type = "Added",
                Location = $"IoModules.{name}",
                Message = $"I/O module '{name}' added",
                Value2 = $"Type: {module.Type}, Location: {module.Location ?? "N/A"}"
            });
        }

        // Find removed modules
        foreach (var name in names1.Except(names2))
        {
            var module = modules1.First(m => m.Name == name);
            details.Add(new DiffDetail
            {
                Type = "Removed",
                Location = $"IoModules.{name}",
                Message = $"I/O module '{name}' removed",
                Value1 = $"Type: {module.Type}, Location: {module.Location ?? "N/A"}"
            });
        }

        // Find modified modules
        foreach (var name in names1.Intersect(names2))
        {
            var module1 = modules1.First(m => m.Name == name);
            var module2 = modules2.First(m => m.Name == name);

            if (module1.Type != module2.Type)
            {
                details.Add(new DiffDetail
                {
                    Type = "Modified",
                    Location = $"IoModules.{name}.Type",
                    Message = $"I/O module '{name}' type changed",
                    Value1 = module1.Type,
                    Value2 = module2.Type
                });
            }

            if (module1.Location != module2.Location)
            {
                details.Add(new DiffDetail
                {
                    Type = "Modified",
                    Location = $"IoModules.{name}.Location",
                    Message = $"I/O module '{name}' location changed",
                    Value1 = module1.Location ?? "N/A",
                    Value2 = module2.Location ?? "N/A"
                });
            }

            if (module1.CatalogNumber != module2.CatalogNumber)
            {
                details.Add(new DiffDetail
                {
                    Type = "Modified",
                    Location = $"IoModules.{name}.CatalogNumber",
                    Message = $"I/O module '{name}' catalog number changed",
                    Value1 = module1.CatalogNumber ?? "N/A",
                    Value2 = module2.CatalogNumber ?? "N/A"
                });
            }
        }
    }

    private void CompareMetadata(Dictionary<string, string> metadata1, Dictionary<string, string> metadata2, List<DiffDetail> details)
    {
        var keys1 = metadata1.Keys.ToHashSet();
        var keys2 = metadata2.Keys.ToHashSet();

        // Find added metadata
        foreach (var key in keys2.Except(keys1))
        {
            details.Add(new DiffDetail
            {
                Type = "Info",
                Location = $"Metadata.{key}",
                Message = $"Metadata '{key}' added",
                Value2 = metadata2[key]
            });
        }

        // Find removed metadata
        foreach (var key in keys1.Except(keys2))
        {
            details.Add(new DiffDetail
            {
                Type = "Info",
                Location = $"Metadata.{key}",
                Message = $"Metadata '{key}' removed",
                Value1 = metadata1[key]
            });
        }

        // Find modified metadata
        foreach (var key in keys1.Intersect(keys2))
        {
            if (metadata1[key] != metadata2[key])
            {
                details.Add(new DiffDetail
                {
                    Type = "Info",
                    Location = $"Metadata.{key}",
                    Message = $"Metadata '{key}' changed",
                    Value1 = metadata1[key],
                    Value2 = metadata2[key]
                });
            }
        }
    }

    private string GenerateSummary(List<DiffDetail> details)
    {
        var added = details.Count(d => d.Type == "Added");
        var removed = details.Count(d => d.Type == "Removed");
        var modified = details.Count(d => d.Type == "Modified");
        var info = details.Count(d => d.Type == "Info");

        if (added == 0 && removed == 0 && modified == 0 && info == 0)
        {
            return "No differences found.";
        }

        var parts = new List<string>();
        if (added > 0) parts.Add($"{added} added");
        if (removed > 0) parts.Add($"{removed} removed");
        if (modified > 0) parts.Add($"{modified} modified");
        if (info > 0) parts.Add($"{info} informational");

        return $"Found {details.Count} differences: {string.Join(", ", parts)}";
    }
}

/// <summary>
/// Result of a diff operation.
/// </summary>
public class DiffResult
{
    /// <summary>
    /// Path to the first file.
    /// </summary>
    public string File1 { get; set; } = string.Empty;

    /// <summary>
    /// Path to the second file.
    /// </summary>
    public string File2 { get; set; } = string.Empty;

    /// <summary>
    /// Summary of differences.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Whether differences were found.
    /// </summary>
    public bool HasDifferences { get; set; }

    /// <summary>
    /// Detailed list of differences.
    /// </summary>
    public List<DiffDetail> Details { get; set; } = new();
}

/// <summary>
/// A single difference detail.
/// </summary>
public class DiffDetail
{
    /// <summary>
    /// Type of difference (Added, Removed, Modified, Info).
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Description of the difference.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Location in the project where the difference occurs.
    /// </summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// Value in the first project.
    /// </summary>
    public string? Value1 { get; set; }

    /// <summary>
    /// Value in the second project.
    /// </summary>
    public string? Value2 { get; set; }
}
