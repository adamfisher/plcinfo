using System.Globalization;
using System.Net;
using System.Text;
using PlcInfo.Models;
using PlcInfo.Services;

namespace PlcInfo.Rendering;

/// <summary>
/// Self-contained HTML pages (no external assets): a project report with rendered
/// ladder logic, and a visual diff. All project text is HTML-encoded.
/// </summary>
public static class HtmlReportRenderer
{
    private const string Css = """
        :root { --bg:#ffffff; --fg:#1f2328; --muted:#59636e; --line:#d1d9e0; --panel:#f6f8fa;
                --add:#1a7f37; --add-bg:#dafbe1; --del:#cf222e; --del-bg:#ffebe9; --accent:#0969da; }
        @media (prefers-color-scheme: dark) {
          :root { --bg:#0d1117; --fg:#e6edf3; --muted:#9198a1; --line:#3d444d; --panel:#151b23;
                  --add:#3fb950; --add-bg:#12261e; --del:#f85149; --del-bg:#25171c; --accent:#4493f8; }
        }
        * { box-sizing: border-box; }
        body { margin: 0; padding: 24px 16px 64px; background: var(--bg); color: var(--fg);
               font: 14px/1.5 -apple-system, "Segoe UI", Roboto, Helvetica, Arial, sans-serif; }
        main { max-width: 1200px; margin: 0 auto; }
        h1 { font-size: 24px; margin: 0 0 4px; } h2 { font-size: 18px; margin: 32px 0 8px; border-bottom: 1px solid var(--line); padding-bottom: 4px; }
        h3 { font-size: 15px; margin: 20px 0 6px; } .muted { color: var(--muted); }
        .cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(130px, 1fr)); gap: 8px; margin: 16px 0; }
        .card { background: var(--panel); border: 1px solid var(--line); border-radius: 6px; padding: 8px 12px; }
        .card b { display: block; font-size: 20px; }
        table { border-collapse: collapse; width: 100%; margin: 8px 0; font-size: 13px; }
        th, td { border: 1px solid var(--line); padding: 4px 8px; text-align: left; vertical-align: top; }
        th { background: var(--panel); }
        details { border: 1px solid var(--line); border-radius: 6px; margin: 8px 0; padding: 0 12px; }
        details[open] { padding-bottom: 12px; } summary { cursor: pointer; padding: 8px 0; font-weight: 600; }
        pre, code { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; font-size: 12px; }
        pre { background: var(--panel); border: 1px solid var(--line); border-radius: 6px; padding: 8px; overflow-x: auto; }
        .rung { border-top: 1px solid var(--line); padding: 8px 0; }
        .rung-head { display: flex; gap: 12px; align-items: baseline; }
        .rung-no { font-weight: 600; min-width: 56px; } .comment { color: var(--muted); font-style: italic; }
        .scroll { overflow-x: auto; }
        .ladder { color: var(--fg); }
        .ladder line, .ladder path { stroke: currentColor; stroke-width: 1.5; }
        .ladder .lad-rail { stroke-width: 3; } .ladder .lad-sym { stroke-width: 2; }
        .ladder text { fill: currentColor; font: 11px ui-monospace, Menlo, Consolas, monospace; }
        .ladder .lad-head { font-weight: 700; } .ladder .lad-mark { font-weight: 700; font-size: 10px; }
        .ladder .lad-box { fill: var(--panel); stroke: currentColor; stroke-width: 1.5; }
        .ladder .lad-add { color: var(--add); } .ladder .lad-add .lad-box { fill: var(--add-bg); }
        .ladder .lad-del { color: var(--del); } .ladder .lad-del .lad-box { fill: var(--del-bg); }
        .sev-error { color: var(--del); font-weight: 600; } .sev-warning { color: #9a6700; font-weight: 600; }
        .sides { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
        @media (max-width: 800px) { .sides { grid-template-columns: 1fr; } }
        .side { border: 1px solid var(--line); border-radius: 6px; padding: 8px; overflow-x: auto; }
        .side.old { background: var(--del-bg); } .side.new { background: var(--add-bg); }
        .tag-add { color: var(--add); font-weight: 600; } .tag-del { color: var(--del); font-weight: 600; } .tag-mod { color: var(--accent); font-weight: 600; }
        .diff-line-add { color: var(--add); } .diff-line-del { color: var(--del); }
        """;

    /// <summary>
    /// Renders a full project report.
    /// </summary>
    public static string RenderProject(PlcProject project, IReadOnlyList<LintFinding> findings)
    {
        var sb = new StringBuilder();
        var title = project.Controller?.Name is { Length: > 0 } name ? name : Path.GetFileName(project.FilePath);
        Begin(sb, $"{title} – plcinfo report");

        sb.Append($"<h1>{E(title)}</h1><p class=\"muted\">{E(project.Vendor)} · {E(project.Format)}");
        if (project.Controller is { } controller)
            sb.Append($" · {E(controller.Type)} · firmware {E(controller.FirmwareRevision)}");
        sb.Append("</p>");

        var routines = project.Programs.SelectMany(p => p.Routines).ToList();
        var cards = new (string Label, int Value)[]
        {
            ("Programs", project.Programs.Count), ("Routines", routines.Count),
            ("Rungs", routines.Sum(r => r.Rungs.Count)), ("Blocks", project.Blocks.Count),
            ("Tags", project.Tags.Count + project.Programs.Sum(p => p.LocalTags.Count)),
            ("Data types", project.DataTypes.Count), ("Tasks", project.Tasks.Count), ("I/O modules", project.IoModules.Count)
        };
        sb.Append("<div class=\"cards\">");
        foreach (var (label, value) in cards.Where(c => c.Value > 0))
            sb.Append(CultureInfo.InvariantCulture, $"<div class=\"card\"><b>{value}</b>{E(label)}</div>");
        sb.Append("</div>");

        if (project.Metadata.TryGetValue("Warnings", out var warnings) && !string.IsNullOrWhiteSpace(warnings))
        {
            sb.Append("<h2>Parser warnings</h2><ul>");
            foreach (var warning in warnings.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                sb.Append($"<li>{E(warning)}</li>");
            sb.Append("</ul>");
        }

        RenderFindings(sb, findings);
        RenderTasks(sb, project);

        if (project.Programs.Count > 0)
        {
            sb.Append("<h2>Programs</h2>");
            foreach (var program in project.Programs)
            {
                sb.Append($"<details><summary>{E(program.Name)} <span class=\"muted\">{E(program.Type)} · {program.Routines.Count} routine(s)</span></summary>");
                if (!string.IsNullOrWhiteSpace(program.Description))
                    sb.Append($"<p class=\"comment\">{E(program.Description)}</p>");
                foreach (var routine in program.Routines)
                    RenderRoutine(sb, routine);
                RenderTags(sb, program.LocalTags, "Tags");
                sb.Append("</details>");
            }
        }

        if (project.Blocks.Count > 0)
        {
            sb.Append("<h2>Blocks</h2>");
            foreach (var block in project.Blocks)
            {
                var number = block.Number > 0 ? block.Number.ToString(CultureInfo.InvariantCulture) : "";
                sb.Append($"<details><summary>{E(block.Type)}{number} {E(block.Name)} <span class=\"muted\">{E(block.Language)}</span></summary>");
                if (!string.IsNullOrWhiteSpace(block.Title))
                    sb.Append($"<p class=\"comment\">{E(block.Title)}</p>");
                RenderNetworks(sb, block.Networks);
                if (!string.IsNullOrEmpty(block.Source))
                    sb.Append($"<pre>{E(block.Source)}</pre>");
                sb.Append("</details>");
            }
        }

        RenderTags(sb, project.Tags, "Global tags", heading: true);
        RenderDataTypes(sb, project.DataTypes);
        RenderModules(sb, project.IoModules);

        End(sb);
        return sb.ToString();
    }

    /// <summary>
    /// Renders a diff as HTML. Changed rungs are drawn old and new side by side with the
    /// instructions that were removed (old side) and added (new side) highlighted.
    /// </summary>
    public static string RenderDiff(DiffResult result)
    {
        var sb = new StringBuilder();
        Begin(sb, "plcinfo diff");
        sb.Append($"<h1>Project diff</h1><p class=\"muted\">{E(result.File1)} → {E(result.File2)}</p>");
        sb.Append($"<p><b>{E(result.Summary)}</b></p>");

        foreach (var group in result.Details.GroupBy(d => d.Category).OrderBy(g => g.Key == "Logic" ? 0 : g.Key == "DataType" ? 1 : 2))
        {
            var heading = group.Key switch { "Logic" => "Logic", "DataType" => "Data types", _ => "Project, tags and configuration" };
            sb.Append($"<h2>{E(heading)}</h2>");
            foreach (var detail in group)
                RenderDiffDetail(sb, detail);
        }

        End(sb);
        return sb.ToString();
    }

    private static void RenderDiffDetail(StringBuilder sb, DiffDetail detail)
    {
        var css = detail.Type switch { "Added" => "tag-add", "Removed" => "tag-del", "Modified" => "tag-mod", _ => "muted" };
        sb.Append($"<div class=\"rung\"><div><span class=\"{css}\">{E(detail.Type)}</span> <code>{E(detail.Location)}</code></div><div>{E(detail.Message)}</div>");

        var oldLadder = LadderParser.TryParse(detail.Value1);
        var newLadder = LadderParser.TryParse(detail.Value2);
        if (detail.Category == "Logic" && (oldLadder != null || newLadder != null))
        {
            var oldKeys = Keys(oldLadder);
            var newKeys = Keys(newLadder);
            sb.Append("<div class=\"sides\">");
            if (oldLadder != null)
            {
                sb.Append("<div class=\"side old\"><div class=\"muted\">before</div>");
                sb.Append(LadderSvgRenderer.Render(oldLadder, i => newKeys.Contains(i.Key) ? null : "lad-del"));
                sb.Append($"<div><code>{E(detail.Value1)}</code></div></div>");
            }
            if (newLadder != null)
            {
                sb.Append("<div class=\"side new\"><div class=\"muted\">after</div>");
                sb.Append(LadderSvgRenderer.Render(newLadder, i => oldKeys.Contains(i.Key) ? null : "lad-add"));
                sb.Append($"<div><code>{E(detail.Value2)}</code></div></div>");
            }
            sb.Append("</div>");
        }
        else if (detail.Category == "Logic" && (detail.Value1 != null || detail.Value2 != null))
        {
            sb.Append("<pre>");
            foreach (var line in Lines(detail.Value1))
                sb.Append($"<span class=\"diff-line-del\">- {E(line)}</span>\n");
            foreach (var line in Lines(detail.Value2))
                sb.Append($"<span class=\"diff-line-add\">+ {E(line)}</span>\n");
            sb.Append("</pre>");
        }
        else if (detail.Value1 != null || detail.Value2 != null)
        {
            sb.Append($"<div class=\"muted\">{E(detail.Value1 ?? "(none)")} → {E(detail.Value2 ?? "(none)")}</div>");
        }

        sb.Append("</div>");
    }

    private static HashSet<string> Keys(LadderSeries? rung)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        void Walk(LadderNode node)
        {
            switch (node)
            {
                case LadderInstruction i: keys.Add(i.Key); break;
                case LadderSeries s: foreach (var n in s.Items) Walk(n); break;
                case LadderBranch b: foreach (var l in b.Legs) Walk(l); break;
            }
        }
        if (rung != null)
            Walk(rung);
        return keys;
    }

    private static void RenderRoutine(StringBuilder sb, RoutineInfo routine)
    {
        sb.Append($"<h3>{E(routine.Name)} <span class=\"muted\">{E(routine.Language)}</span></h3>");
        if (!string.IsNullOrWhiteSpace(routine.Description))
            sb.Append($"<p class=\"comment\">{E(routine.Description)}</p>");

        foreach (var rung in routine.Rungs)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<div class=\"rung\"><div class=\"rung-head\"><span class=\"rung-no\">Rung {rung.Number}</span>");
            if (!string.IsNullOrWhiteSpace(rung.Comment))
                sb.Append($"<span class=\"comment\">{E(rung.Comment)}</span>");
            if (rung.EditState is not (null or "N"))
                sb.Append($"<span class=\"sev-warning\">online edit: {E(rung.EditState)}</span>");
            sb.Append("</div>");

            var ladder = LadderParser.TryParse(rung.Text);
            if (ladder != null)
                sb.Append($"<div class=\"scroll\">{LadderSvgRenderer.Render(ladder)}</div>");
            sb.Append($"<code>{E(rung.Text ?? "")}</code></div>");
        }

        RenderNetworks(sb, routine.Networks);

        if (!string.IsNullOrEmpty(routine.Source))
            sb.Append($"<pre>{E(routine.Source)}</pre>");
    }

    private static void RenderNetworks(StringBuilder sb, IReadOnlyList<NetworkInfo> networks)
    {
        foreach (var network in networks)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<div class=\"rung\"><div class=\"rung-head\"><span class=\"rung-no\">Network {network.Number}</span>");
            if (!string.IsNullOrWhiteSpace(network.Title))
                sb.Append($"<b>{E(network.Title)}</b>");
            if (!string.IsNullOrWhiteSpace(network.Comment))
                sb.Append($"<span class=\"comment\">{E(network.Comment)}</span>");
            sb.Append("</div>");

            // PLCopen LD networks use rung-style text and can be drawn; others are shown as text.
            var ladder = network.Language is "LD" ? LadderParser.TryParse(network.Text) : null;
            if (ladder != null)
                sb.Append($"<div class=\"scroll\">{LadderSvgRenderer.Render(ladder)}</div>");
            else if (!string.IsNullOrEmpty(network.Text))
                sb.Append($"<pre>{E(network.Text)}</pre>");
            sb.Append("</div>");
        }
    }

    private static void RenderFindings(StringBuilder sb, IReadOnlyList<LintFinding> findings)
    {
        sb.Append($"<h2>Lint</h2>");
        if (findings.Count == 0)
        {
            sb.Append("<p class=\"muted\">No findings.</p>");
            return;
        }
        sb.Append("<table><tr><th>Severity</th><th>Code</th><th>Location</th><th>Message</th></tr>");
        foreach (var finding in findings)
        {
            var severity = finding.Severity.ToString().ToLowerInvariant();
            sb.Append($"<tr><td class=\"sev-{severity}\">{E(severity)}</td><td>{E(finding.Code)}</td><td><code>{E(finding.Location)}</code></td><td>{E(finding.Message)}</td></tr>");
        }
        sb.Append("</table>");
    }

    private static void RenderTasks(StringBuilder sb, PlcProject project)
    {
        if (project.Tasks.Count == 0)
            return;
        sb.Append("<h2>Tasks</h2><table><tr><th>Name</th><th>Type</th><th>Priority</th><th>Rate (ms)</th><th>Programs</th></tr>");
        foreach (var task in project.Tasks)
        {
            var rate = task.Rate?.ToString(CultureInfo.InvariantCulture) ?? "";
            var state = task.IsEnabled ? "" : " <span class=\"sev-warning\">inhibited</span>";
            sb.Append(CultureInfo.InvariantCulture,
                $"<tr><td>{E(task.Name)}{state}</td><td>{E(task.Type)}</td><td>{task.Priority}</td><td>{E(rate)}</td><td>{E(string.Join(", ", task.ProgramNames))}</td></tr>");
        }
        sb.Append("</table>");
    }

    private static void RenderTags(StringBuilder sb, IReadOnlyList<TagInfo> tags, string title, bool heading = false)
    {
        if (tags.Count == 0)
            return;
        sb.Append(heading ? $"<h2>{E(title)}</h2>" : $"<h3>{E(title)}</h3>");
        sb.Append("<table><tr><th>Name</th><th>Data type</th><th>Scope</th><th>Usage</th><th>Address</th><th>Description</th></tr>");
        foreach (var tag in tags)
        {
            sb.Append($"<tr><td><code>{E(tag.Name)}</code></td><td>{E(tag.DataType)}</td><td>{E(tag.Scope)}</td><td>{E(tag.Usage ?? "")}</td><td>{E(tag.Address ?? "")}</td><td>{E(tag.Description ?? "")}</td></tr>");
        }
        sb.Append("</table>");
    }

    private static void RenderDataTypes(StringBuilder sb, IReadOnlyList<DataTypeInfo> dataTypes)
    {
        if (dataTypes.Count == 0)
            return;
        sb.Append("<h2>Data types</h2>");
        foreach (var dataType in dataTypes)
        {
            sb.Append($"<details><summary>{E(dataType.Name)} <span class=\"muted\">{E(dataType.Kind)}</span></summary>");
            if (!string.IsNullOrWhiteSpace(dataType.Description))
                sb.Append($"<p class=\"comment\">{E(dataType.Description)}</p>");
            sb.Append("<table><tr><th>Member</th><th>Type</th><th>Value</th><th>Description</th></tr>");
            foreach (var member in dataType.Members)
            {
                var dims = member.Dimensions != null ? $"[{member.Dimensions}]" : "";
                sb.Append($"<tr><td><code>{E(member.Name)}</code></td><td>{E(member.DataType + dims)}</td><td>{E(member.InitialValue ?? "")}</td><td>{E(member.Description ?? "")}</td></tr>");
            }
            sb.Append("</table></details>");
        }
    }

    private static void RenderModules(StringBuilder sb, IReadOnlyList<IoModuleInfo> modules)
    {
        if (modules.Count == 0)
            return;
        sb.Append("<h2>I/O modules</h2><table><tr><th>Name</th><th>Catalog</th><th>Type</th><th>Parent</th><th>Location</th></tr>");
        foreach (var module in modules)
            sb.Append($"<tr><td>{E(module.Name)}</td><td>{E(module.CatalogNumber ?? "")}</td><td>{E(module.Type)}</td><td>{E(module.ParentModule ?? "")}</td><td>{E(module.Location ?? "")}</td></tr>");
        sb.Append("</table>");
    }

    private static void Begin(StringBuilder sb, string title)
    {
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append($"<title>{E(title)}</title><style>{Css}</style></head><body><main>");
    }

    private static void End(StringBuilder sb) =>
        sb.Append("<p class=\"muted\">Generated by plcinfo.</p></main></body></html>\n");

    private static IEnumerable<string> Lines(string? value) =>
        string.IsNullOrEmpty(value) ? Enumerable.Empty<string>() : value.Replace("\r\n", "\n").Split('\n');

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? "");
}
