using PlcInfo.Models;
using PlcInfo.Formatters;

namespace PlcInfo.Services;

/// <summary>
/// Service for searching within PLC projects.
/// </summary>
public class SearchService
{
    /// <summary>
    /// Search for text within a PLC project.
    /// </summary>
    public IEnumerable<SearchResult> Search(PlcProject project, string searchText, string? kind = null)
    {
        var results = new List<SearchResult>();

        if (string.IsNullOrWhiteSpace(searchText))
            return results;

        var searchLower = searchText.ToLower();

        // Search in tags
        if (kind == null || kind.Equals("tag", StringComparison.OrdinalIgnoreCase))
        {
            results.AddRange(SearchTags(project, searchLower));
        }

        // Search in comments
        if (kind == null || kind.Equals("comment", StringComparison.OrdinalIgnoreCase))
        {
            results.AddRange(SearchComments(project, searchLower));
        }

        // Search in instructions
        if (kind == null || kind.Equals("instruction", StringComparison.OrdinalIgnoreCase))
        {
            results.AddRange(SearchInstructions(project, searchLower));
        }

        return results;
    }

    private IEnumerable<SearchResult> SearchTags(PlcProject project, string searchText)
    {
        var results = new List<SearchResult>();

        // Search global tags
        foreach (var tag in project.Tags)
        {
            if (tag.Name.ToLower().Contains(searchText) ||
                (tag.Description?.ToLower().Contains(searchText) ?? false) ||
                tag.DataType.ToLower().Contains(searchText))
            {
                results.Add(new SearchResult
                {
                    Type = "Tag",
                    Name = tag.Name,
                    Location = $"Scope: {tag.Scope}",
                    Context = $"{tag.DataType} - {tag.Description ?? "No description"}"
                });
            }
        }

        // Search local tags in programs
        foreach (var program in project.Programs)
        {
            foreach (var tag in program.LocalTags)
            {
                if (tag.Name.ToLower().Contains(searchText) ||
                    (tag.Description?.ToLower().Contains(searchText) ?? false) ||
                    tag.DataType.ToLower().Contains(searchText))
                {
                    results.Add(new SearchResult
                    {
                        Type = "Tag",
                        Name = tag.Name,
                        Location = $"Program: {program.Name}, Scope: {tag.Scope}",
                        Context = $"{tag.DataType} - {tag.Description ?? "No description"}"
                    });
                }
            }
        }

        return results;
    }

    private IEnumerable<SearchResult> SearchComments(PlcProject project, string searchText)
    {
        var results = new List<SearchResult>();

        // Search in program descriptions
        foreach (var program in project.Programs)
        {
            if (program.Description?.ToLower().Contains(searchText) ?? false)
            {
                results.Add(new SearchResult
                {
                    Type = "Comment",
                    Name = $"Program: {program.Name}",
                    Location = program.Type,
                    Context = program.Description
                });
            }

            // Search in routine descriptions
            foreach (var routine in program.Routines)
            {
                if (routine.Description?.ToLower().Contains(searchText) ?? false)
                {
                    results.Add(new SearchResult
                    {
                        Type = "Comment",
                        Name = $"Routine: {routine.Name}",
                        Location = $"Program: {program.Name}",
                        Context = routine.Description
                    });
                }

                // Search in rung comments
                foreach (var rung in routine.Rungs)
                {
                    if (rung.Comment?.ToLower().Contains(searchText) ?? false)
                    {
                        results.Add(new SearchResult
                        {
                            Type = "Comment",
                            Name = $"Rung {rung.Number}",
                            Location = $"Routine: {routine.Name}, Program: {program.Name}",
                            Context = rung.Comment
                        });
                    }
                }
            }
        }

        // Search in block comments
        foreach (var block in project.Blocks)
        {
            if (block.Title?.ToLower().Contains(searchText) ?? false)
            {
                results.Add(new SearchResult
                {
                    Type = "Comment",
                    Name = $"Block: {block.Name}",
                    Location = block.Type,
                    Context = block.Title
                });
            }

            if (block.Comment?.ToLower().Contains(searchText) ?? false)
            {
                results.Add(new SearchResult
                {
                    Type = "Comment",
                    Name = $"Block: {block.Name}",
                    Location = block.Type,
                    Context = block.Comment
                });
            }
        }

        return results;
    }

    private IEnumerable<SearchResult> SearchInstructions(PlcProject project, string searchText)
    {
        var results = new List<SearchResult>();

        foreach (var program in project.Programs)
        {
            foreach (var routine in program.Routines)
            {
                foreach (var rung in routine.Rungs)
                {
                    // Search in rung text
                    if (rung.Text?.ToLower().Contains(searchText) ?? false)
                    {
                        results.Add(new SearchResult
                        {
                            Type = "Instruction",
                            Name = $"Rung {rung.Number}",
                            Location = $"Routine: {routine.Name}, Program: {program.Name}",
                            Context = TruncateText(rung.Text, 100)
                        });
                    }

                    // Search in individual instructions
                    foreach (var instruction in rung.Instructions)
                    {
                        if (instruction.Mnemonic.ToLower().Contains(searchText) ||
                            instruction.Operands.Any(op => op.ToLower().Contains(searchText)))
                        {
                            var operandList = string.Join(", ", instruction.Operands);
                            results.Add(new SearchResult
                            {
                                Type = "Instruction",
                                Name = instruction.Mnemonic,
                                Location = $"Rung {rung.Number}, Routine: {routine.Name}, Program: {program.Name}",
                                Context = operandList
                            });
                        }
                    }
                }
            }
        }

        return results;
    }

    private string TruncateText(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;

        return text.Substring(0, maxLength - 3) + "...";
    }
}