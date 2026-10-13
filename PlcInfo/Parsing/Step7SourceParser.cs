using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PlcInfo.Models;

namespace PlcInfo.Parsing;

/// <summary>
/// Parses Step 7 / TIA Portal text sources: STL (.awl) and SCL (.scl), as produced by
/// "Generate source from blocks".
/// </summary>
/// <remarks>
/// Envelope (spec §8): <c>ORGANIZATION_BLOCK|FUNCTION_BLOCK|FUNCTION|DATA_BLOCK|TYPE … END_…</c>,
/// with headers either absolute (<c>FUNCTION_BLOCK FB 10</c>) or symbolic
/// (<c>FUNCTION_BLOCK "MOTOR v4.3"</c>, or unquoted in SCL: <c>FUNCTION Str_Clear : VOID</c>); a declaration part (<c>TITLE =</c>, <c>VERSION :</c>,
/// <c>VAR_INPUT … END_VAR</c>, <c>STRUCT … END_STRUCT</c>) up to <c>BEGIN</c>; then the body.
/// STL bodies are split into <c>NETWORK</c> sections, each with an optional <c>TITLE =</c> and
/// <c>//</c> comment lines. The grammar is UNVERIFIED against the Siemens manuals (not
/// retrievable); it follows the real exports used as fixtures (planken/Step7 MOTOR.awl,
/// Smart_Factory *.scl).
/// </remarks>
internal static class Step7SourceParser
{
    private static readonly Regex BlockHeader = new(
        @"^(?<kw>ORGANIZATION_BLOCK|FUNCTION_BLOCK|FUNCTION|DATA_BLOCK|TYPE)\s+(?:(?<type>OB|FB|FC|DB|UDT)\s*(?<num>\d{1,9})\b|""(?<sym>[^""]+)""|(?<sym>[A-Za-z_][A-Za-z0-9_]*))(?<rest>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex BlockEnd = new(
        @"^END_(ORGANIZATION_BLOCK|FUNCTION_BLOCK|FUNCTION|DATA_BLOCK|TYPE)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex HeaderProperty = new(
        @"^(?<key>TITLE|AUTHOR|FAMILY|NAME|VERSION)\s*[:=]\s*(?<value>.*?)\s*;?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex Member = new(
        @"^(?<name>""[^""]+""|[A-Za-z_][A-Za-z0-9_]*)\s*(?:\{[^}]*\})?\s*:\s*(?<type>[^;]*?)\s*(?::=\s*(?<init>[^;]*?)\s*)?;?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Dictionary<string, string> Sections = new(StringComparer.OrdinalIgnoreCase)
    {
        ["VAR_INPUT"] = "Input",
        ["VAR_OUTPUT"] = "Output",
        ["VAR_IN_OUT"] = "InOut",
        ["VAR"] = "Static",
        ["VAR_STAT"] = "Static",
        ["VAR_TEMP"] = "Temp",
        ["VAR CONSTANT"] = "Constant",
        ["VAR_CONSTANT"] = "Constant"
    };

    /// <summary>
    /// Result of parsing a source file.
    /// </summary>
    internal sealed record Result(List<BlockInfo> Blocks, List<DataTypeInfo> DataTypes, List<TagInfo> Tags);

    /// <summary>
    /// Parses STL (<paramref name="language"/> "STL") or SCL ("SCL") source text.
    /// </summary>
    internal static Result Parse(string content, string fileName, string language)
    {
        var result = new Result(new List<BlockInfo>(), new List<DataTypeInfo>(), new List<TagInfo>());
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        int i = 0;
        while (i < lines.Length)
        {
            var header = BlockHeader.Match(lines[i].Trim().TrimStart('﻿'));
            if (!header.Success)
            {
                i++;
                continue;
            }

            var end = i + 1;
            while (end < lines.Length && !BlockEnd.IsMatch(lines[end].Trim()))
                end++;
            var blockLines = lines[i..Math.Min(end + 1, lines.Length)];
            ParseUnit(header, blockLines, fileName, language, result);
            i = end + 1;
        }

        return result;
    }

    private static void ParseUnit(Match header, string[] lines, string fileName, string language, Result result)
    {
        var keyword = header.Groups["kw"].Value.ToUpperInvariant();
        var type = keyword switch
        {
            "ORGANIZATION_BLOCK" => "OB",
            "FUNCTION_BLOCK" => "FB",
            "FUNCTION" => "FC",
            "DATA_BLOCK" => "DB",
            _ => "UDT"
        };
        var symbol = header.Groups["sym"].Success ? header.Groups["sym"].Value : null;
        int.TryParse(header.Groups["num"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number);
        var name = symbol ?? $"{type}{number}";

        // Declaration part: everything up to BEGIN (or the end, for a TYPE). S7-SCL code blocks
        // may omit BEGIN (planken/Step7 STR_CLEAR.scl); the statements then follow the last END_VAR.
        var beginIndex = Array.FindIndex(lines, 1, l => l.Trim().Equals("BEGIN", StringComparison.OrdinalIgnoreCase));
        if (beginIndex < 0 && language == "SCL" && type is "OB" or "FB" or "FC")
            beginIndex = Array.FindLastIndex(lines, l => l.Trim().TrimEnd(';').Equals("END_VAR", StringComparison.OrdinalIgnoreCase));
        var declarationEnd = beginIndex < 0 ? lines.Length - 1 : beginIndex;
        var declaration = ParseDeclaration(lines, 1, declarationEnd);

        if (type == "UDT")
        {
            result.DataTypes.Add(new DataTypeInfo
            {
                Name = name,
                Kind = "Struct",
                Description = declaration.Title,
                Members = declaration.Members.Select(m => new DataTypeMember
                {
                    Name = m.Path,
                    DataType = m.DataType,
                    Description = m.Comment,
                    InitialValue = m.Init
                }).ToList(),
                Properties = new Dictionary<string, string> { ["FileName"] = fileName }
            });
            return;
        }

        var block = new BlockInfo
        {
            Name = name,
            Number = number,
            Type = type,
            Language = type == "DB" ? "DB" : language,
            Title = declaration.Title,
            Comment = declaration.Comment,
            SizeBytes = Encoding.UTF8.GetByteCount(string.Join("\n", lines)),
            Properties = new Dictionary<string, string>
            {
                ["FileName"] = fileName,
                ["SourceType"] = language == "SCL" ? "SCL" : "AWL"
            }
        };
        if (symbol != null)
            block.Properties["Symbol"] = symbol;
        foreach (var (key, value) in declaration.Properties)
            block.Properties[key] = value;
        var returnType = Regex.Match(header.Groups["rest"].Value, @"^\s*:\s*(?<t>\S+)");
        if (type == "FC" && returnType.Success)
            block.Properties["ReturnType"] = returnType.Groups["t"].Value;

        foreach (var member in declaration.Members.Where(m => !m.Path.Contains('.')))
            block.Interface[$"{member.Section}.{member.Path}"] = member.DataType;

        if (type == "DB")
        {
            foreach (var member in declaration.Members)
            {
                result.Tags.Add(new TagInfo
                {
                    Name = member.Path,
                    DataType = member.DataType,
                    Scope = name,
                    Usage = member.Section,
                    Description = member.Comment,
                    InitialValue = member.Init
                });
            }
        }
        else if (beginIndex >= 0)
        {
            var bodyEnd = BlockEnd.IsMatch(lines[^1].Trim()) ? lines.Length - 1 : lines.Length;
            var body = lines[(beginIndex + 1)..Math.Max(beginIndex + 1, bodyEnd)];
            block.Networks = language == "SCL" ? SclNetworks(body) : StlNetworks(body);
            block.NetworkCount = block.Networks.Count;
            block.Source = TrimBlankLines(body);
        }

        result.Blocks.Add(block);
    }

    // ---- declarations ------------------------------------------------------------

    private sealed record DeclaredMember(string Section, string Path, string DataType, string? Init, string? Comment);

    private sealed class Declaration
    {
        public string? Title;
        public string? Comment;
        public readonly Dictionary<string, string> Properties = new();
        public readonly List<DeclaredMember> Members = new();
    }

    private static Declaration ParseDeclaration(string[] lines, int start, int end)
    {
        var declaration = new Declaration();
        string? section = null;
        var structPath = new Stack<string>();
        var commentAfterTitle = new List<string>();
        var inTitleComment = false;

        for (int i = start; i < end; i++)
        {
            var raw = lines[i].Trim();
            if (raw.Length == 0)
            {
                inTitleComment = false;
                continue;
            }

            if (raw.StartsWith("//", StringComparison.Ordinal))
            {
                if (inTitleComment)
                    commentAfterTitle.Add(raw[2..].Trim());
                continue;
            }
            inTitleComment = false;

            var (code, comment) = SplitComment(raw);
            var upper = code.TrimEnd(';').Trim().ToUpperInvariant();

            if (section == null)
            {
                var property = HeaderProperty.Match(code);
                if (property.Success)
                {
                    var key = property.Groups["key"].Value.ToUpperInvariant();
                    var value = property.Groups["value"].Value.Trim().Trim('\'', '"');
                    if (key == "TITLE")
                    {
                        declaration.Title ??= value.Length > 0 ? value : null;
                        inTitleComment = true;
                    }
                    else
                    {
                        declaration.Properties[CultureInfo.InvariantCulture.TextInfo.ToTitleCase(key.ToLowerInvariant())] = value;
                    }
                    continue;
                }
            }

            if (Sections.TryGetValue(upper, out var sectionName))
            {
                section = sectionName;
                structPath.Clear();
                continue;
            }
            if (upper == "END_VAR")
            {
                section = null;
                continue;
            }
            if (upper == "STRUCT" && structPath.Count == 0 && section == null)
            {
                // DATA_BLOCK / TYPE body: an unnamed top-level STRUCT.
                section = "Static";
                continue;
            }
            if (upper == "END_STRUCT")
            {
                if (structPath.Count > 0)
                    structPath.Pop();
                else
                    section = null;
                continue;
            }

            if (section == null)
                continue;

            var member = Member.Match(code);
            if (!member.Success)
                continue;

            var name = member.Groups["name"].Value.Trim('"');
            var path = structPath.Count == 0 ? name : string.Join(".", structPath.Reverse()) + "." + name;
            var dataType = member.Groups["type"].Value.Trim();
            var isStruct = dataType.Equals("STRUCT", StringComparison.OrdinalIgnoreCase);
            declaration.Members.Add(new DeclaredMember(
                section,
                path,
                isStruct ? "Struct" : Unquote(dataType),
                member.Groups["init"].Success ? member.Groups["init"].Value.Trim() : null,
                comment));
            if (isStruct)
                structPath.Push(name);
        }

        if (commentAfterTitle.Count > 0)
            declaration.Comment = string.Join("\n", commentAfterTitle);
        return declaration;
    }

    // ---- bodies ------------------------------------------------------------------

    private static List<NetworkInfo> StlNetworks(string[] body)
    {
        var networks = new List<NetworkInfo>();
        NetworkInfo? current = null;
        List<string> text = new();
        List<string> comment = new();
        List<InstructionInfo> instructions = new();
        var seenStatement = false;
        var inCall = false;

        void Flush()
        {
            if (current == null)
                return;
            current.Comment = comment.Count > 0 ? string.Join("\n", comment) : null;
            current.Text = string.Join("\n", text);
            current.Instructions = instructions;
            networks.Add(current);
        }

        foreach (var line in body)
        {
            var trimmed = line.Trim();
            if (trimmed.Equals("NETWORK", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                current = new NetworkInfo { Number = networks.Count + 1, Language = "STL" };
                text = new List<string>();
                comment = new List<string>();
                instructions = new List<InstructionInfo>();
                seenStatement = false;
                inCall = false;
                continue;
            }

            if (current == null || trimmed.Length == 0)
                continue;

            if (!seenStatement && current.Title == null && trimmed.StartsWith("TITLE", StringComparison.OrdinalIgnoreCase))
            {
                var title = Regex.Match(trimmed, @"^TITLE\s*=\s*(.*)$", RegexOptions.IgnoreCase);
                if (title.Success)
                {
                    current.Title = title.Groups[1].Value.Trim();
                    if (current.Title.Length == 0)
                        current.Title = null;
                    continue;
                }
            }

            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                if (!seenStatement)
                    comment.Add(trimmed[2..].Trim());
                else
                    text.Add("//" + trimmed[2..].TrimEnd());
                continue;
            }

            seenStatement = true;
            var (code, inlineComment) = SplitComment(trimmed);
            var statement = Whitespace.Replace(code.TrimEnd().TrimEnd(';').Trim(), " ");
            text.Add(inlineComment != null ? $"{statement} //{inlineComment}" : statement);

            if (inCall)
            {
                // CALL parameter lines: "IN := L 1.0," … "PT := #x);"
                if (instructions.Count > 0)
                {
                    var call = instructions[^1];
                    call.Operands = call.Operands.Append(statement.TrimEnd(',', ')').Trim()).ToList();
                }
                inCall = !statement.EndsWith(')');
                continue;
            }

            var tokens = statement.Split(' ', 2);
            var label = (string?)null;
            if (tokens[0].EndsWith(':') && tokens.Length > 1)
            {
                label = tokens[0].TrimEnd(':');
                tokens = tokens[1].Split(' ', 2);
            }
            var mnemonic = tokens[0];
            var operand = tokens.Length > 1 ? tokens[1].Trim() : null;
            if (mnemonic.Equals("CALL", StringComparison.OrdinalIgnoreCase) && operand != null && operand.EndsWith('('))
            {
                inCall = true;
                operand = operand.TrimEnd('(').Trim();
            }

            var info = new InstructionInfo
            {
                Mnemonic = mnemonic,
                Type = "Statement",
                Operands = operand is { Length: > 0 } ? new List<string> { operand } : new List<string>(),
                Position = instructions.Count
            };
            if (label != null)
                info.Properties["Label"] = label;
            instructions.Add(info);
        }

        Flush();
        return networks;
    }

    private static List<NetworkInfo> SclNetworks(string[] body)
    {
        var text = TrimBlankLines(body);
        return text.Length == 0
            ? new List<NetworkInfo>()
            : new List<NetworkInfo> { new() { Number = 1, Language = "SCL", Text = text } };
    }

    /// <summary>
    /// Joins lines with trailing whitespace removed and leading/trailing blank lines dropped.
    /// </summary>
    private static string TrimBlankLines(IEnumerable<string> body)
    {
        var lines = body.Select(l => l.TrimEnd()).ToList();
        while (lines.Count > 0 && lines[0].Length == 0)
            lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Splits a line at a <c>//</c> comment that is not inside a quoted symbol or string.
    /// </summary>
    private static (string Code, string? Comment) SplitComment(string line)
    {
        var inDouble = false;
        var inSingle = false;
        for (int i = 0; i < line.Length - 1; i++)
        {
            var c = line[i];
            if (c == '"' && !inSingle) inDouble = !inDouble;
            else if (c == '\'' && !inDouble) inSingle = !inSingle;
            else if (c == '/' && line[i + 1] == '/' && !inDouble && !inSingle)
            {
                var comment = line[(i + 2)..].Trim();
                return (line[..i].TrimEnd(), comment.Length > 0 ? comment : null);
            }
        }
        return (line, null);
    }

    private static string Unquote(string type)
    {
        var t = type.Trim();
        return t.Length >= 2 && t[0] == '"' && t[^1] == '"' && t.IndexOf('"', 1) == t.Length - 1 ? t[1..^1] : t;
    }
}
