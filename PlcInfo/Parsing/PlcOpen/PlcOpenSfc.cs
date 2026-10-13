using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PlcInfo.Models;

namespace PlcInfo.Parsing.PlcOpen;

/// <summary>
/// Summarises a PLCopen TC6 SFC body (§"sfcObjects") as one network whose text lists the steps
/// and transitions in document order:
/// <code>
/// INITIAL STEP Init
///   ACTION N Fill
/// STEP Fill
/// TRANSITION Init -> Fill WHEN start
/// TRANSITION Fill -> JUMP(Init) WHEN done
/// </code>
/// Source and target steps are found by following the wiring through selection and
/// simultaneous divergences and convergences.
/// </summary>
internal static class PlcOpenSfc
{
    private const int MaxDepth = 256;

    /// <summary>
    /// Builds the summary network for an SFC element.
    /// </summary>
    public static NetworkInfo Summarize(XElement sfc, string context, List<string> warnings)
    {
        var graph = new PlcOpenGraph(sfc, context, warnings);
        var nodes = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var element in sfc.Elements())
        {
            var id = element.Attribute("localId")?.Value;
            if (id != null)
                nodes.TryAdd(id, element);
        }

        // For each transition, the steps it leads to.
        var targets = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var element in sfc.Elements().Where(e => e.Name.LocalName is "step" or "macroStep" or "jumpStep"))
        {
            var target = element.Name.LocalName == "jumpStep"
                ? $"JUMP({element.Attribute("targetName")?.Value})"
                : element.Attribute("name")?.Value ?? "?";
            foreach (var transition in Walk(element, nodes, backwardThrough: ["selectionConvergence", "simultaneousDivergence"], stopAt: "transition"))
            {
                var id = transition.Attribute("localId")!.Value;
                if (!targets.TryGetValue(id, out var list))
                    targets[id] = list = new List<string>();
                if (!list.Contains(target))
                    list.Add(target);
            }
        }

        var lines = new List<string>();
        var instructions = new List<InstructionInfo>();
        int steps = 0, transitions = 0;

        foreach (var element in sfc.Elements())
        {
            switch (element.Name.LocalName)
            {
                case "step":
                case "macroStep":
                {
                    steps++;
                    var name = element.Attribute("name")?.Value ?? "?";
                    var initial = string.Equals(element.Attribute("initialStep")?.Value, "true", StringComparison.OrdinalIgnoreCase);
                    var kind = element.Name.LocalName == "macroStep" ? "MACRO STEP" : "STEP";
                    lines.Add(initial ? $"INITIAL {kind} {name}" : $"{kind} {name}");
                    instructions.Add(new InstructionInfo { Mnemonic = "STEP", Type = "Step", Operands = [name] });

                    var id = element.Attribute("localId")?.Value;
                    foreach (var actionBlock in sfc.Elements("actionBlock").Where(a =>
                                 a.Element("connectionPointIn")?.Elements("connection").Any(c => c.Attribute("refLocalId")?.Value == id) == true))
                    {
                        foreach (var action in actionBlock.Elements("action"))
                            lines.Add("  ACTION " + ActionText(action));
                    }
                    break;
                }
                case "transition":
                {
                    transitions++;
                    var id = element.Attribute("localId")?.Value ?? string.Empty;
                    var from = Walk(element, nodes, backwardThrough: ["selectionDivergence", "simultaneousConvergence"], stopAt: "step|macroStep")
                        .Select(s => s.Attribute("name")?.Value ?? "?")
                        .Distinct()
                        .ToList();
                    var to = targets.GetValueOrDefault(id) ?? new List<string>();
                    var condition = ConditionText(element.Element("condition"), graph);
                    var priority = element.Attribute("priority")?.Value;
                    lines.Add($"TRANSITION {Names(from)} -> {Names(to)} WHEN {condition}" +
                              (string.IsNullOrEmpty(priority) ? string.Empty : $" PRIORITY {priority}"));
                    instructions.Add(new InstructionInfo { Mnemonic = "TRANSITION", Type = "Transition", Operands = [condition] });
                    break;
                }
            }
        }

        for (var i = 0; i < instructions.Count; i++)
            instructions[i].Position = i;

        return new NetworkInfo
        {
            Number = 1,
            Language = "SFC",
            Text = string.Join("\n", lines),
            Instructions = instructions,
            Properties = new Dictionary<string, string>
            {
                ["StepCount"] = steps.ToString(CultureInfo.InvariantCulture),
                ["TransitionCount"] = transitions.ToString(CultureInfo.InvariantCulture)
            }
        };
    }

    private static string Names(List<string> names) => names.Count == 0 ? "?" : string.Join(", ", names);

    /// <summary>
    /// Follows connectionPointIn wiring backwards from <paramref name="start"/> through the given
    /// branch elements and returns the first elements whose kind matches <paramref name="stopAt"/>.
    /// </summary>
    private static IEnumerable<XElement> Walk(XElement start, Dictionary<string, XElement> nodes, string[] backwardThrough, string stopAt)
    {
        var stop = new HashSet<string>(stopAt.Split('|'));
        var seen = new HashSet<string>();
        var queue = new Queue<(XElement Element, int Depth)>();
        queue.Enqueue((start, 0));
        while (queue.Count > 0)
        {
            var (element, depth) = queue.Dequeue();
            if (depth > MaxDepth)
                yield break;
            foreach (var connection in element.Elements("connectionPointIn").Elements("connection"))
            {
                var id = connection.Attribute("refLocalId")?.Value;
                if (id == null || !seen.Add(id) || !nodes.TryGetValue(id, out var source))
                    continue;
                var kind = source.Name.LocalName;
                if (stop.Contains(kind))
                    yield return source;
                else if (backwardThrough.Contains(kind))
                    queue.Enqueue((source, depth + 1));
            }
        }
    }

    private static string ConditionText(XElement? condition, PlcOpenGraph graph)
    {
        if (condition == null)
            return "?";

        string text;
        if (condition.Element("reference") is { } reference)
            text = reference.Attribute("name")?.Value ?? "?";
        else if (condition.Element("inline") is { } inline)
            text = InlineText(inline) ?? inline.Attribute("name")?.Value.NullIfEmpty() ?? "?";
        else if (condition.Element("connectionPointIn") is { } input)
            text = graph.RenderInput(input);
        else
            text = "?";

        return string.Equals(condition.Attribute("negated")?.Value, "true", StringComparison.OrdinalIgnoreCase) ? $"NOT({text})" : text;
    }

    private static string ActionText(XElement action)
    {
        var qualifier = action.Attribute("qualifier")?.Value ?? "N";
        var target = action.Element("reference")?.Attribute("name")?.Value
                     ?? InlineText(action.Element("inline")) ?? "inline";
        var duration = action.Attribute("duration")?.Value;
        var indicator = action.Attribute("indicator")?.Value;
        return $"{qualifier} {target}" +
               (string.IsNullOrEmpty(duration) ? string.Empty : $" {duration}") +
               (string.IsNullOrEmpty(indicator) ? string.Empty : $" INDICATOR {indicator}");
    }

    // An inline body is a ppx:body, so the ST or IL element is its direct child (tc6_xml_v201.xsd, transition/condition/inline).
    private static string? InlineText(XElement? inline)
    {
        var text = PlcOpenText.FormattedText(inline?.Elements().FirstOrDefault(e => e.Name.LocalName is "ST" or "IL"));
        return string.IsNullOrWhiteSpace(text) ? null : Collapse(text);
    }

    private static string Collapse(string text) => Regex.Replace(text.Trim(), @"\s+", " ");
}
