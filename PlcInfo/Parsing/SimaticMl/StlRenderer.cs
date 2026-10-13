using System.Xml.Linq;
using PlcInfo.Models;

namespace PlcInfo.Parsing.SimaticMl;

/// <summary>
/// Turns an STL <c>StatementList</c> network into statement text, one statement per line.
/// </summary>
/// <remarks>
/// Each <c>StlStatement</c> holds an <c>StlToken Text=".."</c> (the operation), optional operand
/// <c>Access</c> elements and an optional <c>LineComment</c>. Most token names are the English
/// mnemonic; a few are spelled out. The mapping below is taken from a real V18 export
/// (Smart_Factory FC_DI.xml: <c>Assign</c> for <c>=</c>, <c>Rise</c>/<c>Fall</c> for
/// <c>FP</c>/<c>FN</c>, <c>EMPTY_LINE</c>, <c>COMMENT</c>); unknown names are kept verbatim.
/// </remarks>
internal static class StlRenderer
{
    private static readonly Dictionary<string, string> Mnemonics = new(StringComparer.Ordinal)
    {
        ["Assign"] = "=",
        ["Rise"] = "FP",
        ["Fall"] = "FN"
    };

    /// <summary>
    /// Renders <paramref name="statementList"/> to text and instructions.
    /// </summary>
    internal static (string Text, List<InstructionInfo> Instructions) Render(XElement statementList)
    {
        var lines = new List<string>();
        var instructions = new List<InstructionInfo>();

        foreach (var statement in statementList.Children("StlStatement"))
        {
            var token = (string?)statement.Child("StlToken")?.Attribute("Text");
            var comment = RenderComment(statement.Child("LineComment"));
            var label = (string?)statement.Child("LabelDeclaration")?.Child("Label")?.Attribute("Name");

            if (token == "EMPTY_LINE")
            {
                lines.Add(string.Empty);
                continue;
            }

            if (token == "COMMENT" || token == null)
            {
                lines.Add(comment ?? string.Empty);
                continue;
            }

            var mnemonic = Mnemonics.TryGetValue(token, out var mapped) ? mapped : token;
            var operands = new List<string>();
            var textParts = new List<string>();
            foreach (var access in statement.Elements().Where(e => e.Name.LocalName == "Access"))
            {
                if ((string?)access.Attribute("Scope") == "Call")
                {
                    var call = RenderCall(access).ToList();
                    operands.AddRange(call);
                    textParts.Add($"{call[0]}({string.Join(", ", call.Skip(1))})");
                }
                else
                {
                    var operand = SimaticMlOperand.Render(access);
                    operands.Add(operand);
                    textParts.Add(operand);
                }
            }

            var line = (label != null ? label + ": " : string.Empty) + mnemonic;
            if (textParts.Count > 0)
                line += " " + string.Join(", ", textParts);
            if (comment != null)
                line += " " + comment;
            lines.Add(line);

            instructions.Add(new InstructionInfo
            {
                Mnemonic = mnemonic,
                Type = "Statement",
                Operands = operands,
                Position = instructions.Count
            });
        }

        // Trailing empty lines carry no logic.
        while (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        return (string.Join("\n", lines), instructions);
    }

    /// <summary>
    /// A <c>CALL</c> operand: an <c>Instruction</c> (e.g. NORM_X with TemplateValue types, as in
    /// Smart_Factory FC_AnalogInCoverter.xml) or a block <c>CallInfo</c> with an optional instance,
    /// followed by one "PARAM := value" entry per <c>Parameter</c>.
    /// </summary>
    private static IEnumerable<string> RenderCall(XElement access)
    {
        var instruction = access.Child("Instruction");
        var callInfo = access.Child("CallInfo");
        var target = instruction ?? callInfo;
        if (target == null)
            return new[] { "<Call>" };

        string head;
        if (instruction != null)
        {
            head = (string?)instruction.Attribute("Name") ?? "?";
            var types = instruction.Children("TemplateValue")
                .Where(t => (string?)t.Attribute("Type") == "Type")
                .Select(t => t.Value.Trim())
                .ToList();
            if (types.Count > 0)
                head += "<" + string.Join(", ", types) + ">";
        }
        else
        {
            head = $"\"{(string?)callInfo!.Attribute("Name")}\"";
            var instance = callInfo.Child("Instance");
            if (instance != null)
                head += ", " + SimaticMlOperand.Render(instance);
        }

        var result = new List<string> { head };
        foreach (var parameter in target.Children("Parameter"))
        {
            var value = parameter.Elements().FirstOrDefault(e => e.Name.LocalName == "Access");
            if (value != null)
                result.Add($"{(string?)parameter.Attribute("Name")} := {SimaticMlOperand.Render(value)}");
        }
        return result;
    }

    private static string? RenderComment(XElement? lineComment)
    {
        if (lineComment == null)
            return null;
        var text = string.Concat(lineComment.Children("Text").Select(t => t.Value));
        return (string?)lineComment.Attribute("Inserted") == "true" ? $"(*{text}*)" : $"//{text}";
    }
}
