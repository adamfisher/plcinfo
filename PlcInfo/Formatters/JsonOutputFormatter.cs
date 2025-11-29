using System.Text.Json;
using System.Text.Json.Serialization;
using PlcInfo.Models;

namespace PlcInfo.Formatters;

/// <summary>
/// JSON output formatter.
/// </summary>
public class JsonOutputFormatter : IOutputFormatter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <inheritdoc/>
    public string FormatSummary(PlcProject project)
    {
        var summary = new
        {
            project.FilePath,
            project.Vendor,
            project.Format,
            project.LastModified,
            Controller = project.Controller == null ? null : new
            {
                project.Controller.Name,
                project.Controller.Type,
                project.Controller.CpuType,
                project.Controller.FirmwareRevision
            },
            Statistics = new
            {
                Tasks = project.Tasks.Count,
                Programs = project.Programs.Count,
                Blocks = project.Blocks.Count,
                Routines = project.Programs.Sum(p => p.Routines.Count),
                Tags = project.Tags.Count,
                IoModules = project.IoModules.Count,
                TotalRungs = project.Programs.SelectMany(p => p.Routines).Sum(r => r.RungCount),
                TotalNetworks = project.Blocks.Sum(b => b.NetworkCount)
            }
        };

        return JsonSerializer.Serialize(summary, Options);
    }

    /// <inheritdoc/>
    public string FormatStructure(PlcProject project)
    {
        var structure = new
        {
            Controller = project.Controller == null ? null : new
            {
                project.Controller.Name,
                project.Controller.Type,
                project.Controller.CpuType
            },
            Tasks = project.Tasks.Select(t => new
            {
                t.Name,
                t.Type,
                t.Priority,
                t.Rate,
                t.ProgramNames
            }),
            Programs = project.Programs.Select(p => new
            {
                p.Name,
                p.Type,
                p.Language,
                Routines = p.Routines.Select(r => new
                {
                    r.Name,
                    r.Type,
                    r.Language,
                    r.RungCount,
                    r.NetworkCount
                })
            }),
            Blocks = project.Blocks.Select(b => new
            {
                b.Name,
                b.Number,
                b.Type,
                b.Language,
                b.NetworkCount,
                b.Title
            })
        };

        return JsonSerializer.Serialize(structure, Options);
    }

    /// <inheritdoc/>
    public string FormatTags(PlcProject project, TagFilter? filter = null)
    {
        var tags = FilterTags(project.Tags, filter);

        var result = new
        {
            Total = tags.Count(),
            Tags = tags.Select(t => new
            {
                t.Name,
                t.DataType,
                t.Scope,
                t.Usage,
                t.Description,
                t.InitialValue,
                t.Address,
                t.IsIoTag,
                t.IoModule,
                t.ArrayDimensions,
                t.AliasFor
            })
        };

        return JsonSerializer.Serialize(result, Options);
    }

    /// <inheritdoc/>
    public string FormatIo(PlcProject project)
    {
        var ioTags = project.Tags.Where(t => t.IsIoTag).ToList();

        var result = new
        {
            Modules = project.IoModules.Select(m => new
            {
                m.Name,
                m.Type,
                m.CatalogNumber,
                m.Vendor,
                m.Location,
                m.ChannelCount,
                m.CommFormat,
                m.ParentModule,
                AssociatedTags = project.Tags
                    .Where(t => t.IsIoTag && (t.IoModule == m.Name || m.AssociatedTags.Contains(t.Name)))
                    .Select(t => new { t.Name, t.DataType, t.Address })
            }),
            IoTagSummary = new
            {
                Total = ioTags.Count,
                Inputs = ioTags.Count(t =>
                    t.Address?.StartsWith("I", StringComparison.OrdinalIgnoreCase) == true ||
                    t.Address?.StartsWith("%I", StringComparison.OrdinalIgnoreCase) == true),
                Outputs = ioTags.Count(t =>
                    t.Address?.StartsWith("Q", StringComparison.OrdinalIgnoreCase) == true ||
                    t.Address?.StartsWith("%Q", StringComparison.OrdinalIgnoreCase) == true)
            }
        };

        return JsonSerializer.Serialize(result, Options);
    }

    /// <inheritdoc/>
    public string FormatMetrics(MetricsSummary metrics)
    {
        return JsonSerializer.Serialize(metrics, Options);
    }

    /// <inheritdoc/>
    public string FormatSearchResults(IEnumerable<SearchResult> results)
    {
        var resultList = results.ToList();

        var result = new
        {
            Total = resultList.Count,
            Results = resultList
        };

        return JsonSerializer.Serialize(result, Options);
    }

    /// <inheritdoc/>
    public string FormatDiff(Services.DiffResult result)
    {
        return JsonSerializer.Serialize(result, Options);
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
}