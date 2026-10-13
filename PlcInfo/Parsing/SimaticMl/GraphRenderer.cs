using System.Text;
using System.Xml.Linq;
using PlcInfo.Models;

namespace PlcInfo.Parsing.SimaticMl;

/// <summary>
/// Summarizes a GRAPH (sequence) network: steps, transitions with their conditions, and the
/// step/transition connections.
/// </summary>
/// <remarks>
/// Element names follow a real V21 export (Czarnak/simaticml-decoder StateMachine.xml):
/// <c>Graph/Sequence/Steps/Step[@Number,@Name,@Init]</c>, <c>Transitions/Transition[@Number,@Name]</c>
/// holding a LAD/FBD <c>FlgNet</c> condition, and <c>Connections/Connection</c> with
/// <c>NodeFrom</c>/<c>NodeTo</c> refs and a <c>LinkType</c>. The spec has no element details
/// for GRAPH, so step actions are listed in simplified form only.
/// </remarks>
internal static class GraphRenderer
{
    /// <summary>
    /// Result of rendering a GRAPH network.
    /// </summary>
    internal sealed record Result(string Text, List<InstructionInfo> Instructions, int Steps, int Transitions, bool HasActions);

    /// <summary>
    /// Renders <paramref name="graph"/>.
    /// </summary>
    internal static Result Render(XElement graph)
    {
        var lines = new List<string>();
        var instructions = new List<InstructionInfo>();
        int steps = 0, transitions = 0;
        var hasActions = false;

        foreach (var sequence in graph.Children("Sequence"))
        {
            foreach (var step in sequence.Child("Steps").Children("Step"))
            {
                steps++;
                var header = new StringBuilder($"STEP {Attr(step, "Number")} {Attr(step, "Name")}");
                if (Attr(step, "Init") == "true")
                    header.Append(" INIT");
                lines.Add(header.ToString());

                foreach (var action in step.Child("Actions").Children("Action").Where(a => a.HasElements))
                {
                    hasActions = true;
                    lines.Add("  ACTION " + FlattenAction(action));
                }

                AddCondition(lines, instructions, "SUPERVISION", step.Child("Supervisions").Children("Supervision"));
                AddCondition(lines, instructions, "INTERLOCK", step.Child("Interlocks").Children("Interlock"));
            }

            foreach (var transition in sequence.Child("Transitions").Children("Transition"))
            {
                transitions++;
                var flgNet = transition.Child("FlgNet");
                var condition = flgNet != null ? FlgNetRenderer.Render(flgNet) : (Text: string.Empty, Instructions: new List<InstructionInfo>());
                lines.Add($"TRANSITION {Attr(transition, "Number")} {Attr(transition, "Name")}: {condition.Text.Replace("\n", "; ")}");
                Append(instructions, condition.Instructions);
            }

            foreach (var connection in sequence.Child("Connections").Children("Connection"))
            {
                var link = connection.Child("LinkType")?.Value;
                var line = $"{NodeRef(connection.Child("NodeFrom"))} -> {NodeRef(connection.Child("NodeTo"))}";
                lines.Add(string.IsNullOrEmpty(link) || link == "Direct" ? line : $"{line} ({link})");
            }
        }

        return new Result(string.Join("\n", lines), instructions, steps, transitions, hasActions);
    }

    private static void AddCondition(List<string> lines, List<InstructionInfo> instructions, string label, IEnumerable<XElement> conditions)
    {
        foreach (var condition in conditions)
        {
            var flgNet = condition.Child("FlgNet");
            if (flgNet == null)
                continue;
            var rendered = FlgNetRenderer.Render(flgNet);
            // An unconditional supervision/interlock coil is the default; only list real conditions.
            if (rendered.Instructions.Count <= 1)
                continue;
            lines.Add($"  {label}: {rendered.Text.Replace("\n", "; ")}");
            Append(instructions, rendered.Instructions);
        }
    }

    private static void Append(List<InstructionInfo> target, IEnumerable<InstructionInfo> items)
    {
        foreach (var item in items)
            target.Add(item with { Position = target.Count });
    }

    private static string NodeRef(XElement? node)
    {
        var target = node?.Elements().FirstOrDefault();
        if (target == null)
            return "?";
        var number = (string?)target.Attribute("Number") ?? string.Empty;
        return target.Name.LocalName switch
        {
            "StepRef" => "S" + number,
            "TransitionRef" => "T" + number,
            "BranchRef" => "B" + number,
            var other => other + number
        };
    }

    private static string FlattenAction(XElement action)
    {
        var parts = new List<string>();
        foreach (var node in action.Descendants())
        {
            if (node.Name.LocalName == "Access" && node.Parent?.Name.LocalName != "Component")
                parts.Add(SimaticMlOperand.Render(node));
            else if (node.Attribute("Text") is { } text && node.Ancestors().All(a => a.Name.LocalName != "Access"))
                parts.Add(text.Value.Trim());
        }
        return string.Join(" ", parts.Where(p => p.Length > 0));
    }

    private static string Attr(XElement element, string name) =>
        ((string?)element.Attribute(name) ?? string.Empty).Trim();
}
