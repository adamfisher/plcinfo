using System.Globalization;
using System.Text;
using System.Xml.Linq;
using PlcInfo.Models;

namespace PlcInfo.Parsing.SimaticMl;

/// <summary>
/// Rebuilds SCL source text from a tokenized <c>StructuredText</c> network.
/// </summary>
/// <remarks>
/// Spec §3 (SCL): source is stored as a token stream: <c>Token Text</c>, <c>Blank Num</c>,
/// <c>NewLine Num</c>, <c>Text</c> (region names), <c>LineComment</c>, and <c>Access</c>
/// operands. Concatenating the tokens in order and re-adding <c>#</c> before locals and quotes
/// around globals gives the source. Calls are <c>Access Scope="Call"</c> wrapping an
/// <c>Instruction</c> (instruction or instance call) or a <c>CallInfo</c> (block call), whose
/// <c>Parameter</c> children carry their own tokens. <c>TemplateValue</c> elements are type
/// annotations that do not appear in the source.
/// </remarks>
internal sealed class SclRenderer
{
    private readonly List<InstructionInfo> _instructions = new();
    private int _suppressRecording;

    /// <summary>
    /// Renders <paramref name="structuredText"/> to source text and the calls it contains.
    /// </summary>
    internal static (string Text, List<InstructionInfo> Instructions) Render(XElement structuredText)
    {
        var renderer = new SclRenderer();
        var sb = new StringBuilder();
        string? previousOperand = null;
        foreach (var child in structuredText.Elements())
            renderer.Append(sb, child, ref previousOperand);
        return (Normalize(sb.ToString()), renderer._instructions);
    }

    private string RenderNode(XElement node)
    {
        var sb = new StringBuilder();
        string? previous = null;
        Append(sb, node, ref previous);
        return sb.ToString();
    }

    private void AppendChildren(StringBuilder sb, XElement parent, Func<XElement, bool>? skip = null)
    {
        string? previous = null;
        foreach (var child in parent.Elements())
        {
            if (skip != null && skip(child))
                continue;
            Append(sb, child, ref previous);
        }
    }

    private void Append(StringBuilder sb, XElement node, ref string? previousOperand)
    {
        switch (node.Name.LocalName)
        {
            case "Token":
                sb.Append((string?)node.Attribute("Text"));
                return;
            case "Blank":
                sb.Append(' ', Count(node));
                return;
            case "NewLine":
                sb.Append('\n', Count(node));
                return;
            case "Text":
                sb.Append(node.Value);
                return;
            case "LineComment":
                AppendComment(sb, node);
                return;
            case "TemplateValue":
                return;
            case "Access":
                if ((string?)node.Attribute("Scope") == "Call")
                {
                    AppendCall(sb, node, previousOperand);
                    previousOperand = null;
                }
                else
                {
                    var operand = SimaticMlOperand.Render(node, RenderNode);
                    sb.Append(operand);
                    previousOperand = operand;
                }
                return;
            case "Parameter":
                sb.Append((string?)node.Attribute("Name"));
                AppendChildren(sb, node);
                return;
            case "Instruction":
                sb.Append((string?)node.Attribute("Name"));
                AppendChildren(sb, node);
                return;
            case "CallInfo":
                AppendCallInfo(sb, node);
                return;
            default:
                if (node.HasElements)
                    AppendChildren(sb, node);
                else
                    sb.Append(node.Value);
                return;
        }
    }

    private void AppendCall(StringBuilder sb, XElement access, string? instanceOperand)
    {
        AppendChildren(sb, access);
        if (_suppressRecording > 0)
            return;

        // An instance call (TON_1(...)) has an unnamed Instruction preceded by the instance operand.
        var instruction = access.Child("Instruction");
        var callInfo = access.Child("CallInfo");
        var mnemonic = (string?)instruction?.Attribute("Name")
                       ?? (string?)callInfo?.Attribute("Name")
                       ?? instanceOperand
                       ?? "CALL";

        var parameters = (instruction ?? callInfo)?.Children("Parameter") ?? Enumerable.Empty<XElement>();
        // Parameters are rendered a second time for the operand list; nested calls inside
        // them were already recorded by the pass above.
        _suppressRecording++;
        var operands = parameters
            .Select(p => $"{(string?)p.Attribute("Name")}{Normalize(RenderChildren(p))}".Trim())
            .ToList();
        _suppressRecording--;

        var info = new InstructionInfo
        {
            Mnemonic = mnemonic,
            Type = "Call",
            Operands = operands,
            Position = _instructions.Count
        };
        if (instruction?.Attribute("Name") == null && instanceOperand != null)
            info.Properties["Instance"] = instanceOperand;
        if (callInfo != null)
            info.Properties["BlockType"] = (string?)callInfo.Attribute("BlockType") ?? string.Empty;
        _instructions.Add(info);
    }

    private void AppendCallInfo(StringBuilder sb, XElement callInfo)
    {
        var instance = callInfo.Child("Instance");
        sb.Append(instance != null
            ? SimaticMlOperand.Render(instance, RenderNode)
            : $"\"{(string?)callInfo.Attribute("Name")}\"");
        AppendChildren(sb, callInfo, c => c.Name.LocalName == "Instance");
    }

    private string RenderChildren(XElement parent)
    {
        var sb = new StringBuilder();
        AppendChildren(sb, parent);
        return sb.ToString();
    }

    private static void AppendComment(StringBuilder sb, XElement comment)
    {
        var inserted = (string?)comment.Attribute("Inserted") == "true";
        sb.Append(inserted ? "(*" : "//");
        foreach (var child in comment.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "NewLine":
                    sb.Append('\n', Count(child));
                    break;
                case "Blank":
                    sb.Append(' ', Count(child));
                    break;
                default:
                    sb.Append(child.Value);
                    break;
            }
        }
        if (inserted && (string?)comment.Attribute("NoClosingBracket") != "true")
            sb.Append("*)");
    }

    private static int Count(XElement node) =>
        int.TryParse((string?)node.Attribute("Num"), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is > 0 and < 10_000
            ? n
            : 1;

    /// <summary>
    /// Unifies line endings, strips trailing whitespace on each line and trailing blank lines.
    /// </summary>
    private static string Normalize(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select(l => l.TrimEnd())
            .ToList();
        while (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return string.Join("\n", lines);
    }
}
