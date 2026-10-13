using System.Globalization;
using System.Text;
using PlcInfo.Models;

namespace PlcInfo.Formatters;

/// <summary>
/// Renders a project as deterministic, line-oriented text for line-based diff tools
/// (git textconv, code review). Every line is self-describing (it repeats its context,
/// e.g. the program and routine), so a hunk in <c>git diff</c> is understandable on its
/// own. Volatile data (file path, timestamps, export metadata) is omitted so that
/// re-exporting an unchanged project produces identical output.
/// </summary>
public static class CanonicalTextRenderer
{
    /// <summary>
    /// Version of the output layout. Bump when the layout changes.
    /// </summary>
    public const int LayoutVersion = 1;

    public static string Render(PlcProject project)
    {
        var sb = new StringBuilder();
        void Line(string text) => sb.Append(text).Append('\n');

        Line($"# plcinfo textconv v{LayoutVersion}: {project.Vendor} {project.Format}");

        if (project.Controller is { } controller)
        {
            Line($"controller {controller.Name}: type={controller.Type} firmware={controller.FirmwareRevision}");
        }

        foreach (var task in project.Tasks)
        {
            var rate = task.Rate.HasValue ? $" rate={Num(task.Rate.Value)}ms" : "";
            var watchdog = task.WatchdogTimeout.HasValue ? $" watchdog={Num(task.WatchdogTimeout.Value)}" : "";
            var disabled = task.IsEnabled ? "" : " inhibited";
            Line($"task {task.Name}: type={task.Type} priority={Num(task.Priority)}{rate}{watchdog}{disabled} programs=[{string.Join(", ", task.ProgramNames)}]");
        }

        foreach (var module in project.IoModules)
        {
            Line($"module {module.Name}: catalog={module.CatalogNumber} type={module.Type} parent={module.ParentModule} location={module.Location}");
        }

        foreach (var dataType in project.DataTypes)
        {
            var baseType = dataType.BaseType != null ? $" base={dataType.BaseType}" : "";
            Line($"datatype {dataType.Name}: kind={dataType.Kind}{baseType}{Describe(dataType.Description)}");
            foreach (var member in dataType.Members)
            {
                var dims = member.Dimensions != null ? $"[{member.Dimensions}]" : "";
                var value = member.InitialValue != null ? $" := {OneLine(member.InitialValue)}" : "";
                Line($"datatype {dataType.Name}.{member.Name}: {member.DataType}{dims}{value}{Describe(member.Description)}");
            }
        }

        foreach (var tag in project.Tags)
            RenderTag(Line, tag.Scope, tag);

        foreach (var program in project.Programs)
        {
            Line($"program {program.Name}: type={program.Type}{Describe(program.Description)}");
            foreach (var tag in program.LocalTags)
                RenderTag(Line, program.Name, tag);

            foreach (var routine in program.Routines)
            {
                var prefix = $"{program.Name}/{routine.Name}";
                Line($"routine {prefix}: language={routine.Language}{Describe(routine.Description)}");
                // Rung numbers are deliberately left out: Logix renumbers every later rung
                // on insert/delete, which would make one edit rewrite the rest of the routine.
                foreach (var rung in routine.Rungs)
                {
                    var state = rung.EditState is null or "N" ? "" : $" [edit:{rung.EditState}]";
                    if (!string.IsNullOrWhiteSpace(rung.Comment))
                    {
                        foreach (var commentLine in SplitLines(rung.Comment))
                            Line($"{prefix} rung // {commentLine}");
                    }
                    Line($"{prefix} rung{state}: {OneLine(rung.Text ?? "")}");
                }
                RenderSource(Line, prefix, routine.Source);
                RenderNetworks(Line, prefix, routine.Networks);
            }
        }

        foreach (var block in project.Blocks)
        {
            var prefix = $"{block.Type}{(block.Number > 0 ? Num(block.Number) : "")} {block.Name}";
            Line($"block {prefix}: language={block.Language}{Describe(block.Title)}");
            foreach (var member in block.Interface.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                Line($"{prefix} interface {member.Key}: {member.Value}");
            RenderSource(Line, prefix, block.Source);
            RenderNetworks(Line, prefix, block.Networks);
        }

        return sb.ToString();
    }

    private static void RenderTag(Action<string> line, string scope, TagInfo tag)
    {
        var dims = tag.ArrayDimensions is { Length: > 0 } d ? $"[{string.Join(",", d.Select(Num))}]" : "";
        var parts = new List<string> { $"{tag.DataType}{dims}" };
        if (!string.IsNullOrEmpty(tag.Usage)) parts.Add($"usage={tag.Usage}");
        if (!string.IsNullOrEmpty(tag.AliasFor)) parts.Add($"alias={tag.AliasFor}");
        if (!string.IsNullOrEmpty(tag.Address)) parts.Add($"address={tag.Address}");
        if (!string.IsNullOrEmpty(tag.InitialValue)) parts.Add($":= {OneLine(tag.InitialValue)}");
        line($"tag {scope}.{tag.Name}: {string.Join(" ", parts.Where(p => p.Length > 0))}{Describe(tag.Description)}");
    }

    private static void RenderSource(Action<string> line, string prefix, string? source)
    {
        if (source == null)
            return;
        var lines = SplitLines(source);
        for (int i = 0; i < lines.Length; i++)
            line($"{prefix} {Num(i + 1)}| {lines[i].TrimEnd()}");
    }

    private static void RenderNetworks(Action<string> line, string prefix, IReadOnlyList<NetworkInfo> networks)
    {
        foreach (var network in networks)
        {
            if (!string.IsNullOrWhiteSpace(network.Title))
                line($"{prefix} network {Num(network.Number)} title: {OneLine(network.Title)}");
            if (!string.IsNullOrWhiteSpace(network.Comment))
            {
                foreach (var commentLine in SplitLines(network.Comment))
                    line($"{prefix} network {Num(network.Number)} // {commentLine}");
            }
            foreach (var textLine in SplitLines(network.Text ?? ""))
                line($"{prefix} network {Num(network.Number)}: {textLine.TrimEnd()}");
        }
    }

    private static string Describe(string? description) =>
        string.IsNullOrWhiteSpace(description) ? "" : $"  // {OneLine(description)}";

    private static string OneLine(string text) =>
        string.Join(" ", SplitLines(text).Select(l => l.Trim()).Where(l => l.Length > 0));

    private static string[] SplitLines(string text) => text.Replace("\r\n", "\n").Split('\n');

    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Num(double value) => value.ToString(CultureInfo.InvariantCulture);
}
