using PlcInfo.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PlcInfo.Formatters;

/// <summary>
/// YAML output formatter.
/// </summary>
public class YamlOutputFormatter : IOutputFormatter
{
    internal static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        // Quote strings like "true", "null" or "1.0" so they stay strings on re-read.
        .WithQuotingNecessaryStrings()
        .Build();

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

        return Serializer.Serialize(summary);
    }

    /// <inheritdoc/>
    public string FormatStructure(PlcProject project)
    {
        var structureData = new
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
            }).ToList(),
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
                }).ToList()
            }).ToList(),
            Blocks = project.Blocks.Select(b => new
            {
                b.Name,
                b.Number,
                b.Type,
                b.Language,
                b.NetworkCount,
                b.Title
            }).ToList()
        };

        var result = new { structure = structureData };
        return Serializer.Serialize(result);
    }

    /// <inheritdoc/>
    public string FormatTags(PlcProject project, TagFilter? filter = null)
    {
        var tags = FilterTags(TagFilter.AllTags(project), filter).ToList();

        var tagsData = new
        {
            Total = tags.Count,
            Items = tags.Select(t => new
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
            }).ToList()
        };

        var result = new { tags = tagsData };
        return Serializer.Serialize(result);
    }

    /// <inheritdoc/>
    public string FormatIo(PlcProject project)
    {
        var ioTags = project.Tags.Where(t => t.IsIoTag).ToList();

        var ioData = new
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
                    .ToList()
            }).ToList(),
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

        var result = new { ioModules = ioData.Modules, ioTagSummary = ioData.IoTagSummary };
        return Serializer.Serialize(result);
    }

    /// <inheritdoc/>
    public string FormatMetrics(MetricsSummary metrics)
    {
        var result = new { metrics = metrics };
        return Serializer.Serialize(result);
    }

    /// <inheritdoc/>
    public string FormatSearchResults(IEnumerable<SearchResult> results)
    {
        var resultList = results.ToList();

        var searchData = new
        {
            Total = resultList.Count,
            Results = resultList
        };

        var result = new { searchResults = searchData };
        return Serializer.Serialize(result);
    }

    /// <inheritdoc/>
    public string FormatDiff(Services.DiffResult result)
    {
        var diffData = new { diff = result };
        return Serializer.Serialize(diffData);
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