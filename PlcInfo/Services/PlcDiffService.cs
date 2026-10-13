using System.Globalization;
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
                Value1 = p1.LastModified?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "N/A",
                Value2 = p2.LastModified?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "N/A"
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
        CompareKeyed(tasks1, tasks2, t => t.Name, t => t.Type, "Tasks", "Task",
            t => $"Type: {t.Type}, Priority: {Invariant(t.Priority)}",
            details,
            (name, location, task1, task2) =>
            {
                AddIfChanged(details, location, "Type", $"Task '{name}' type changed", task1.Type, task2.Type);
                AddIfChanged(details, location, "Priority", $"Task '{name}' priority changed",
                    Invariant(task1.Priority), Invariant(task2.Priority));
                if (task1.WatchdogTimeout != task2.WatchdogTimeout)
                {
                    AddIfChanged(details, location, "WatchdogTimeout", $"Task '{name}' watchdog timeout changed",
                        Invariant(task1.WatchdogTimeout) ?? "N/A", Invariant(task2.WatchdogTimeout) ?? "N/A");
                }
            });
    }

    private void ComparePrograms(IReadOnlyList<ProgramInfo> programs1, IReadOnlyList<ProgramInfo> programs2, List<DiffDetail> details)
    {
        CompareKeyed(programs1, programs2, p => p.Name, p => p.Type, "Programs", "Program",
            p => $"Type: {p.Type}, Language: {p.Language}, Routines: {Invariant(p.Routines.Count)}",
            details,
            (name, location, program1, program2) =>
            {
                AddIfChanged(details, location, "Type", $"Program '{name}' type changed", program1.Type, program2.Type);
                AddIfChanged(details, location, "Language", $"Program '{name}' language changed", program1.Language, program2.Language);
                AddIfChanged(details, location, "Routines", $"Program '{name}' routine count changed",
                    Invariant(program1.Routines.Count), Invariant(program2.Routines.Count));

                // Program-scoped tags are compared individually, not just counted.
                CompareTags(program1.LocalTags, program2.LocalTags, details, $"{location}.Tags");
            });
    }

    private void CompareBlocks(IReadOnlyList<BlockInfo> blocks1, IReadOnlyList<BlockInfo> blocks2, List<DiffDetail> details)
    {
        CompareKeyed(blocks1, blocks2, b => b.Name, b => b.Type, "Blocks", "Block",
            b => $"Type: {b.Type}, Language: {b.Language}",
            details,
            (name, location, block1, block2) =>
            {
                AddIfChanged(details, location, "Type", $"Block '{name}' type changed", block1.Type, block2.Type);
                AddIfChanged(details, location, "Language", $"Block '{name}' language changed", block1.Language, block2.Language);
            });
    }

    private void CompareTags(IReadOnlyList<TagInfo> tags1, IReadOnlyList<TagInfo> tags2, List<DiffDetail> details,
        string section = "Tags")
    {
        // Tags with the same name can exist in different scopes (e.g. two GVLs);
        // those are told apart by scope.
        CompareKeyed(tags1, tags2, t => t.Name, t => t.Scope, section, "Tag",
            t => $"Type: {t.DataType}, Scope: {t.Scope}",
            details,
            (name, location, tag1, tag2) =>
            {
                AddIfChanged(details, location, "DataType", $"Tag '{name}' data type changed", tag1.DataType, tag2.DataType);
                AddIfChanged(details, location, "Scope", $"Tag '{name}' scope changed", tag1.Scope, tag2.Scope);
                if (tag1.Usage != tag2.Usage)
                    AddIfChanged(details, location, "Usage", $"Tag '{name}' usage changed", tag1.Usage ?? "N/A", tag2.Usage ?? "N/A");
                if (tag1.InitialValue != tag2.InitialValue)
                    AddIfChanged(details, location, "InitialValue", $"Tag '{name}' initial value changed",
                        tag1.InitialValue ?? "N/A", tag2.InitialValue ?? "N/A");
                if (tag1.Description != tag2.Description)
                {
                    details.Add(new DiffDetail
                    {
                        Type = "Info",
                        Location = $"{location}.Description",
                        Message = $"Tag '{name}' description changed",
                        Value1 = tag1.Description ?? "",
                        Value2 = tag2.Description ?? ""
                    });
                }
            });
    }

    private void CompareIoModules(IReadOnlyList<IoModuleInfo> modules1, IReadOnlyList<IoModuleInfo> modules2, List<DiffDetail> details)
    {
        CompareKeyed(modules1, modules2, m => m.Name, m => m.ParentModule ?? m.Location ?? "", "IoModules", "I/O module",
            m => $"Type: {m.Type}, Location: {m.Location ?? "N/A"}",
            details,
            (name, location, module1, module2) =>
            {
                AddIfChanged(details, location, "Type", $"I/O module '{name}' type changed", module1.Type, module2.Type);
                if (module1.Location != module2.Location)
                    AddIfChanged(details, location, "Location", $"I/O module '{name}' location changed",
                        module1.Location ?? "N/A", module2.Location ?? "N/A");
                if (module1.CatalogNumber != module2.CatalogNumber)
                    AddIfChanged(details, location, "CatalogNumber", $"I/O module '{name}' catalog number changed",
                        module1.CatalogNumber ?? "N/A", module2.CatalogNumber ?? "N/A");
            });
    }

    /// <summary>
    /// Matches items from both sides by key and reports additions, removals and,
    /// via <paramref name="compareMatched"/>, modifications. Items are keyed by name;
    /// when a name is not unique on either side, the key becomes
    /// "{qualifier}.{name}" so same-named items in different scopes are compared
    /// with each other instead of being collapsed. Lookups are O(1).
    /// </summary>
    private static void CompareKeyed<T>(
        IReadOnlyList<T> items1,
        IReadOnlyList<T> items2,
        Func<T, string> nameOf,
        Func<T, string> qualifierOf,
        string section,
        string noun,
        Func<T, string> describe,
        List<DiffDetail> details,
        Action<string, string, T, T> compareMatched)
    {
        var duplicateNames = items1.GroupBy(nameOf).Where(g => g.Count() > 1).Select(g => g.Key)
            .Concat(items2.GroupBy(nameOf).Where(g => g.Count() > 1).Select(g => g.Key))
            .ToHashSet();

        var keyed1 = BuildKeyed(items1, nameOf, qualifierOf, duplicateNames);
        var keyed2 = BuildKeyed(items2, nameOf, qualifierOf, duplicateNames);

        foreach (var (key, item) in keyed2)
        {
            if (keyed1.ContainsKey(key)) continue;
            var name = nameOf(item);
            details.Add(new DiffDetail
            {
                Type = "Added",
                Location = $"{section}.{key}",
                Message = $"{noun} '{name}' added",
                Value2 = describe(item)
            });
        }

        foreach (var (key, item) in keyed1)
        {
            if (keyed2.ContainsKey(key)) continue;
            var name = nameOf(item);
            details.Add(new DiffDetail
            {
                Type = "Removed",
                Location = $"{section}.{key}",
                Message = $"{noun} '{name}' removed",
                Value1 = describe(item)
            });
        }

        foreach (var (key, item1) in keyed1)
        {
            if (keyed2.TryGetValue(key, out var item2))
            {
                compareMatched(nameOf(item1), $"{section}.{key}", item1, item2);
            }
        }
    }

    // Ordered list of (key, item) plus an index, so output follows source order.
    private static OrderedKeyed<T> BuildKeyed<T>(
        IReadOnlyList<T> items, Func<T, string> nameOf, Func<T, string> qualifierOf, HashSet<string> duplicateNames)
    {
        var result = new OrderedKeyed<T>();
        foreach (var item in items)
        {
            var name = nameOf(item);
            var key = duplicateNames.Contains(name) ? $"{qualifierOf(item)}.{name}" : name;

            // Exact duplicates (same qualifier and name) still get a stable, distinct key.
            var unique = key;
            for (int n = 2; result.ContainsKey(unique); n++)
                unique = $"{key}#{n}";

            result.Add(unique, item);
        }
        return result;
    }

    private sealed class OrderedKeyed<T> : IEnumerable<(string Key, T Item)>
    {
        private readonly List<(string Key, T Item)> _ordered = new();
        private readonly Dictionary<string, T> _index = new(StringComparer.Ordinal);

        public void Add(string key, T item)
        {
            _ordered.Add((key, item));
            _index[key] = item;
        }

        public bool ContainsKey(string key) => _index.ContainsKey(key);

        public bool TryGetValue(string key, out T item) => _index.TryGetValue(key, out item!);

        public IEnumerator<(string Key, T Item)> GetEnumerator() => _ordered.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static void AddIfChanged(List<DiffDetail> details, string location, string field, string message,
        string? value1, string? value2)
    {
        if (value1 == value2) return;
        details.Add(new DiffDetail
        {
            Type = "Modified",
            Location = $"{location}.{field}",
            Message = message,
            Value1 = value1,
            Value2 = value2
        });
    }

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string? Invariant(double? value) => value?.ToString(CultureInfo.InvariantCulture);

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

        if (added == 0 && removed == 0 && modified == 0)
        {
            // Informational changes (timestamps, descriptions, metadata) don't count
            // as differences, matching HasDifferences.
            return info == 0
                ? "No differences found."
                : $"No differences found ({info} informational).";
        }

        var parts = new List<string>();
        if (added > 0) parts.Add($"{added} added");
        if (removed > 0) parts.Add($"{removed} removed");
        if (modified > 0) parts.Add($"{modified} modified");
        if (info > 0) parts.Add($"{info} informational");

        return $"Found {added + removed + modified} differences: {string.Join(", ", parts)}";
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
