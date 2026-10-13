using System.Globalization;
using System.Text;
using PlcInfo.Models;

namespace PlcInfo.Services;

/// <summary>
/// Logic-level comparison: routines, rungs, source text, networks and data types.
/// </summary>
public partial class PlcDiffService
{
    /// <summary>Diff categories used by formatters to group details.</summary>
    public static class Categories
    {
        public const string Project = "Project";
        public const string Logic = "Logic";
        public const string DataType = "DataType";
    }

    private void CompareRoutines(IReadOnlyList<RoutineInfo> routines1, IReadOnlyList<RoutineInfo> routines2,
        List<DiffDetail> details, string section)
    {
        CompareKeyed(routines1, routines2, r => r.Name, r => r.Type, section, "Routine",
            r => $"Language: {r.Language}, {DescribeSize(r)}",
            details,
            (name, location, routine1, routine2) =>
            {
                AddIfChanged(details, location, "Language", $"Routine '{name}' language changed", routine1.Language, routine2.Language);
                CompareRungs(routine1.Rungs, routine2.Rungs, details, location, name);
                CompareSource(routine1.Source, routine2.Source, details, location, $"Routine '{name}'");
                CompareNetworks(routine1.Networks, routine2.Networks, details, location, $"Routine '{name}'");
            },
            Categories.Logic);
    }

    private static string DescribeSize(RoutineInfo routine)
    {
        if (routine.Rungs.Count > 0)
            return $"Rungs: {routine.Rungs.Count}";
        if (routine.Networks.Count > 0)
            return $"Networks: {routine.Networks.Count}";
        if (routine.Source != null)
            return $"Lines: {routine.Source.Split('\n').Length}";
        return "empty";
    }

    /// <summary>
    /// Compares ladder rungs by content, not by position: rungs are aligned on their
    /// normalized text, so inserting a rung reports one addition instead of shifting
    /// every following rung. Similar unmatched rungs are reported as modified.
    /// </summary>
    internal void CompareRungs(IReadOnlyList<RungInfo> rungs1, IReadOnlyList<RungInfo> rungs2,
        List<DiffDetail> details, string location, string routineName)
    {
        // Pending/test online edits duplicate rungs; compare the normal (N) rungs only.
        var normal1 = rungs1.Where(IsNormalRung).ToList();
        var normal2 = rungs2.Where(IsNormalRung).ToList();
        var skipped = rungs1.Count - normal1.Count + rungs2.Count - normal2.Count;
        if (skipped > 0)
        {
            details.Add(new DiffDetail
            {
                Type = "Info",
                Category = Categories.Logic,
                Location = $"{location}.Rungs",
                Message = $"Routine '{routineName}' has pending or test online edits; {skipped} edited rung(s) were not compared"
            });
        }

        if (normal1.Count == 0 && normal2.Count == 0)
            return;

        var keys1 = normal1.Select(r => NormalizeRungText(r.Text)).ToList();
        var keys2 = normal2.Select(r => NormalizeRungText(r.Text)).ToList();
        var tokens1 = normal1.Select(RungTokens).ToList();
        var tokens2 = normal2.Select(RungTokens).ToList();

        var steps = SequenceAligner.Align(keys1, keys2,
            (i, j) => SequenceAligner.Jaccard(tokens1[i], tokens2[j]));

        foreach (var step in steps)
        {
            switch (step.Kind)
            {
                case AlignKind.Equal:
                {
                    var old = normal1[step.OldIndex];
                    var @new = normal2[step.NewIndex];
                    if (NormalizeComment(old.Comment) != NormalizeComment(@new.Comment))
                    {
                        details.Add(new DiffDetail
                        {
                            Type = "Info",
                            Category = Categories.Logic,
                            Location = $"{location}.Rungs[{Invariant(@new.Number)}].Comment",
                            Message = $"Rung {Invariant(@new.Number)} comment changed in '{routineName}'",
                            Value1 = old.Comment ?? "",
                            Value2 = @new.Comment ?? ""
                        });
                    }
                    break;
                }
                case AlignKind.Modify:
                {
                    var old = normal1[step.OldIndex];
                    var @new = normal2[step.NewIndex];
                    details.Add(new DiffDetail
                    {
                        Type = "Modified",
                        Category = Categories.Logic,
                        Location = $"{location}.Rungs[{Invariant(@new.Number)}]",
                        Message = $"Rung {Invariant(@new.Number)} changed in '{routineName}': {DescribeInstructionChanges(old, @new)}",
                        Value1 = old.Text,
                        Value2 = @new.Text
                    });
                    break;
                }
                case AlignKind.Delete:
                {
                    var old = normal1[step.OldIndex];
                    details.Add(new DiffDetail
                    {
                        Type = "Removed",
                        Category = Categories.Logic,
                        Location = $"{location}.Rungs[{Invariant(old.Number)}]",
                        Message = $"Rung {Invariant(old.Number)} removed from '{routineName}'",
                        Value1 = old.Text
                    });
                    break;
                }
                case AlignKind.Insert:
                {
                    var @new = normal2[step.NewIndex];
                    details.Add(new DiffDetail
                    {
                        Type = "Added",
                        Category = Categories.Logic,
                        Location = $"{location}.Rungs[{Invariant(@new.Number)}]",
                        Message = $"Rung {Invariant(@new.Number)} added to '{routineName}'",
                        Value2 = @new.Text
                    });
                    break;
                }
            }
        }
    }

    private static bool IsNormalRung(RungInfo rung) => rung.EditState is null or "N";

    /// <summary>
    /// Removes insignificant whitespace (outside string literals) so that "XIC(a) OTE(b)"
    /// and "XIC(a)OTE(b)" compare equal.
    /// </summary>
    internal static string NormalizeRungText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var sb = new StringBuilder(text.Length);
        char quote = '\0';
        foreach (var c in text)
        {
            if (quote != '\0')
            {
                sb.Append(c);
                if (c == quote) quote = '\0';
            }
            else if (c is '\'' or '"')
            {
                quote = c;
                sb.Append(c);
            }
            else if (!char.IsWhiteSpace(c))
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static string NormalizeComment(string? comment) =>
        string.Join(' ', (comment ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static List<string> RungTokens(RungInfo rung)
    {
        var tokens = new List<string>();
        foreach (var instruction in rung.Instructions)
        {
            tokens.Add(instruction.Mnemonic);
            tokens.AddRange(instruction.Operands);
        }
        if (tokens.Count == 0 && !string.IsNullOrEmpty(rung.Text))
            tokens.AddRange(NormalizeRungText(rung.Text).Split('(', ')', ',', '[', ']', ';'));
        return tokens;
    }

    private static string InstructionKey(InstructionInfo instruction) =>
        $"{instruction.Mnemonic}({string.Join(",", instruction.Operands)})";

    /// <summary>
    /// Summarizes which instructions were removed and added, e.g. "-OTE(Motor) +OTL(Motor)".
    /// </summary>
    internal static string DescribeInstructionChanges(RungInfo old, RungInfo @new)
    {
        var oldKeys = old.Instructions.Select(InstructionKey).ToList();
        var newKeys = @new.Instructions.Select(InstructionKey).ToList();
        if (oldKeys.Count == 0 && newKeys.Count == 0)
            return "rung text changed";

        var parts = new List<string>();
        foreach (var step in SequenceAligner.Align(oldKeys, newKeys))
        {
            if (step.Kind == AlignKind.Delete)
                parts.Add("-" + oldKeys[step.OldIndex]);
            else if (step.Kind == AlignKind.Insert)
                parts.Add("+" + newKeys[step.NewIndex]);
        }

        // Same instructions in a different arrangement (e.g. a branch moved).
        return parts.Count == 0 ? "branch structure changed" : string.Join(" ", parts);
    }

    /// <summary>
    /// Compares textual source (ST, SCL, STL) line by line and reports one detail per hunk.
    /// Line numbers are 1-based; added/changed hunks use new-file numbers, removals old-file numbers.
    /// </summary>
    internal void CompareSource(string? source1, string? source2, List<DiffDetail> details, string location, string owner)
    {
        if (source1 == source2)
            return;
        if (source1 == null || source2 == null)
        {
            // Body appeared or disappeared (e.g. language change); the routine-level change covers it.
            if (string.IsNullOrWhiteSpace(source1 ?? source2))
                return;
        }

        var lines1 = SplitLines(source1);
        var lines2 = SplitLines(source2);
        var keys1 = lines1.Select(l => l.TrimEnd()).ToList();
        var keys2 = lines2.Select(l => l.TrimEnd()).ToList();

        var steps = SequenceAligner.Align(keys1, keys2);
        int index = 0;
        while (index < steps.Count)
        {
            if (steps[index].Kind == AlignKind.Equal)
            {
                index++;
                continue;
            }

            var removed = new List<int>();
            var added = new List<int>();
            while (index < steps.Count && steps[index].Kind != AlignKind.Equal)
            {
                if (steps[index].Kind == AlignKind.Delete)
                    removed.Add(steps[index].OldIndex);
                else
                    added.Add(steps[index].NewIndex);
                index++;
            }

            string Range(List<int> lines) => lines.Count == 1
                ? $"L{Invariant(lines[0] + 1)}"
                : $"L{Invariant(lines[0] + 1)}-L{Invariant(lines[^1] + 1)}";

            var type = removed.Count == 0 ? "Added" : added.Count == 0 ? "Removed" : "Modified";
            var range = Range(added.Count > 0 ? added : removed);
            var verb = type switch { "Added" => "added", "Removed" => "removed", _ => "changed" };

            details.Add(new DiffDetail
            {
                Type = type,
                Category = Categories.Logic,
                Location = $"{location}.Source[{range}]",
                Message = $"{owner}: {(Math.Max(added.Count, removed.Count) == 1 ? "line" : "lines")} {range} {verb}",
                Value1 = removed.Count > 0 ? string.Join("\n", removed.Select(i => lines1[i])) : null,
                Value2 = added.Count > 0 ? string.Join("\n", added.Select(i => lines2[i])) : null
            });
        }
    }

    private static List<string> SplitLines(string? source) =>
        string.IsNullOrEmpty(source)
            ? new List<string>()
            : source.Replace("\r\n", "\n").Split('\n').ToList();

    /// <summary>
    /// Compares networks (Siemens LAD/FBD/STL, PLCopen LD) by their canonical text,
    /// aligned like rungs. Title and comment changes are informational.
    /// </summary>
    internal void CompareNetworks(IReadOnlyList<NetworkInfo> networks1, IReadOnlyList<NetworkInfo> networks2,
        List<DiffDetail> details, string location, string owner)
    {
        if (networks1.Count == 0 && networks2.Count == 0)
            return;

        var keys1 = networks1.Select(n => NormalizeRungText(n.Text)).ToList();
        var keys2 = networks2.Select(n => NormalizeRungText(n.Text)).ToList();
        var tokens1 = networks1.Select(NetworkTokens).ToList();
        var tokens2 = networks2.Select(NetworkTokens).ToList();

        foreach (var step in SequenceAligner.Align(keys1, keys2, (i, j) => SequenceAligner.Jaccard(tokens1[i], tokens2[j])))
        {
            var old = step.OldIndex >= 0 ? networks1[step.OldIndex] : null;
            var @new = step.NewIndex >= 0 ? networks2[step.NewIndex] : null;
            switch (step.Kind)
            {
                case AlignKind.Equal:
                    if (old!.Title != @new!.Title || NormalizeComment(old.Comment) != NormalizeComment(@new.Comment))
                    {
                        details.Add(new DiffDetail
                        {
                            Type = "Info",
                            Category = Categories.Logic,
                            Location = $"{location}.Networks[{Invariant(@new.Number)}]",
                            Message = $"{owner}: network {Invariant(@new.Number)} title or comment changed",
                            Value1 = JoinNonEmpty(old.Title, old.Comment),
                            Value2 = JoinNonEmpty(@new.Title, @new.Comment)
                        });
                    }
                    break;
                case AlignKind.Modify:
                    details.Add(new DiffDetail
                    {
                        Type = "Modified",
                        Category = Categories.Logic,
                        Location = $"{location}.Networks[{Invariant(@new!.Number)}]",
                        Message = $"{owner}: network {Invariant(@new.Number)} changed",
                        Value1 = old!.Text,
                        Value2 = @new.Text
                    });
                    break;
                case AlignKind.Delete:
                    details.Add(new DiffDetail
                    {
                        Type = "Removed",
                        Category = Categories.Logic,
                        Location = $"{location}.Networks[{Invariant(old!.Number)}]",
                        Message = $"{owner}: network {Invariant(old.Number)} removed",
                        Value1 = old.Text
                    });
                    break;
                case AlignKind.Insert:
                    details.Add(new DiffDetail
                    {
                        Type = "Added",
                        Category = Categories.Logic,
                        Location = $"{location}.Networks[{Invariant(@new!.Number)}]",
                        Message = $"{owner}: network {Invariant(@new.Number)} added",
                        Value2 = @new.Text
                    });
                    break;
            }
        }
    }

    private static List<string> NetworkTokens(NetworkInfo network)
    {
        var tokens = new List<string>();
        foreach (var instruction in network.Instructions)
        {
            tokens.Add(instruction.Mnemonic);
            tokens.AddRange(instruction.Operands);
        }
        if (tokens.Count == 0 && !string.IsNullOrEmpty(network.Text))
            tokens.AddRange(network.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return tokens;
    }

    private static string JoinNonEmpty(params string?[] parts) =>
        string.Join(" | ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private void CompareDataTypes(IReadOnlyList<DataTypeInfo> types1, IReadOnlyList<DataTypeInfo> types2, List<DiffDetail> details)
    {
        CompareKeyed(types1, types2, t => t.Name, t => t.Kind, "DataTypes", "Data type",
            t => $"Kind: {t.Kind}, Members: {Invariant(t.Members.Count)}",
            details,
            (name, location, type1, type2) =>
            {
                AddIfChanged(details, location, "Kind", $"Data type '{name}' kind changed", type1.Kind, type2.Kind, Categories.DataType);
                AddIfChanged(details, location, "BaseType", $"Data type '{name}' base type changed", type1.BaseType, type2.BaseType, Categories.DataType);

                CompareKeyed(type1.Members, type2.Members, m => m.Name, m => m.DataType, $"{location}.Members", "Member",
                    m => $"Type: {m.DataType}{(m.Dimensions != null ? $"[{m.Dimensions}]" : "")}",
                    details,
                    (memberName, memberLocation, member1, member2) =>
                    {
                        AddIfChanged(details, memberLocation, "DataType", $"Member '{name}.{memberName}' data type changed",
                            member1.DataType, member2.DataType, Categories.DataType);
                        AddIfChanged(details, memberLocation, "Dimensions", $"Member '{name}.{memberName}' dimensions changed",
                            member1.Dimensions, member2.Dimensions, Categories.DataType);
                        AddIfChanged(details, memberLocation, "InitialValue", $"Member '{name}.{memberName}' initial value changed",
                            member1.InitialValue, member2.InitialValue, Categories.DataType);
                    },
                    Categories.DataType);

                // Member order matters for UDT memory layout.
                var order1 = type1.Members.Select(m => m.Name).Where(n => type2.Members.Any(m => m.Name == n)).ToList();
                var order2 = type2.Members.Select(m => m.Name).Where(n => type1.Members.Any(m => m.Name == n)).ToList();
                if (!order1.SequenceEqual(order2))
                {
                    details.Add(new DiffDetail
                    {
                        Type = "Modified",
                        Category = Categories.DataType,
                        Location = $"{location}.Members",
                        Message = $"Data type '{name}' member order changed",
                        Value1 = string.Join(", ", order1),
                        Value2 = string.Join(", ", order2)
                    });
                }
            },
            Categories.DataType);
    }
}
