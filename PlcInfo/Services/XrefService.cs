using System.Text.RegularExpressions;
using PlcInfo.Models;

namespace PlcInfo.Services;

/// <summary>
/// One place where a tag is used.
/// </summary>
public record XrefEntry
{
    /// <summary>Base tag name (without members, bits or indexes), e.g. "Motor" for "Motor.Run[2].0".</summary>
    public string Tag { get; init; } = string.Empty;

    /// <summary>The operand as written, e.g. "Motor.Run[2].0".</summary>
    public string Operand { get; init; } = string.Empty;

    /// <summary>Scope the tag resolved to (a program/POU name, "Controller", a GVL), or null if not defined.</summary>
    public string? Scope { get; init; }

    /// <summary>Read, Write or ReadWrite.</summary>
    public string Access { get; init; } = "Read";

    /// <summary>Program, POU or block containing the use.</summary>
    public string Container { get; init; } = string.Empty;

    /// <summary>Routine (or method/network owner) containing the use.</summary>
    public string Routine { get; init; } = string.Empty;

    /// <summary>Where in the routine: "Rung 3", "Line 12", "Network 2".</summary>
    public string Position { get; init; } = string.Empty;

    /// <summary>The instruction that uses the operand (e.g. "OTE"), or ":=" for an ST assignment.</summary>
    public string Instruction { get; init; } = string.Empty;
}

/// <summary>
/// Builds a cross-reference of where each tag is read and written.
/// </summary>
public class XrefService
{
    // Rockwell instructions that write an operand, by 0-based operand index.
    // Timers/counters/one-shots write their control structure; output-type
    // instructions write their destination. Everything else is a read.
    private static readonly Dictionary<string, int[]> WrittenOperands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["OTE"] = new[] { 0 }, ["OTL"] = new[] { 0 }, ["OTU"] = new[] { 0 },
        ["ONS"] = new[] { 0 }, ["OSR"] = new[] { 0, 1 }, ["OSF"] = new[] { 0, 1 },
        ["TON"] = new[] { 0 }, ["TOF"] = new[] { 0 }, ["RTO"] = new[] { 0 },
        ["TONR"] = new[] { 0 }, ["TOFR"] = new[] { 0 }, ["RTOR"] = new[] { 0 },
        ["CTU"] = new[] { 0 }, ["CTD"] = new[] { 0 }, ["CTUD"] = new[] { 0 }, ["RES"] = new[] { 0 },
        ["MOV"] = new[] { 1 }, ["MOVE"] = new[] { 1 }, ["COP"] = new[] { 1 }, ["CPS"] = new[] { 1 },
        ["FLL"] = new[] { 1 }, ["CLR"] = new[] { 0 }, ["MVM"] = new[] { 2 }, ["BTD"] = new[] { 2 },
        ["CPT"] = new[] { 0 },
        ["ADD"] = new[] { 2 }, ["SUB"] = new[] { 2 }, ["MUL"] = new[] { 2 }, ["DIV"] = new[] { 2 },
        ["MOD"] = new[] { 2 }, ["SQR"] = new[] { 1 }, ["NEG"] = new[] { 1 }, ["ABS"] = new[] { 1 },
        ["AND"] = new[] { 2 }, ["OR"] = new[] { 2 }, ["XOR"] = new[] { 2 }, ["NOT"] = new[] { 1 },
        ["TOD"] = new[] { 1 }, ["FRD"] = new[] { 1 }, ["SWPB"] = new[] { 2 },
        ["GSV"] = new[] { 3 }, ["MSG"] = new[] { 0 },
        ["BSL"] = new[] { 0, 1 }, ["BSR"] = new[] { 0, 1 }, ["FFL"] = new[] { 1, 2 }, ["FFU"] = new[] { 1, 2 },
        ["LFL"] = new[] { 1, 2 }, ["LFU"] = new[] { 1, 2 },
        // IEC / Siemens style coils and assignments used by other parsers' instruction lists.
        ["="] = new[] { 0 }, ["S"] = new[] { 0 }, ["R"] = new[] { 0 },
        ["Coil"] = new[] { 0 }, ["SCoil"] = new[] { 0 }, ["RCoil"] = new[] { 0 }
    };

    // Identifiers in ST, skipping string literals and comments is done before matching.
    private static readonly Regex Identifier = new(@"(?<![\w.#%])#?(?<name>[A-Za-z_][A-Za-z0-9_]*)(?<rest>(?:\.[A-Za-z0-9_]+|\[[^\]]*\])*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Assignment = new(@"^\s*#?(?<target>[A-Za-z_][A-Za-z0-9_.\[\],]*)\s*(:=|\?=|S=|R=)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> StKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "IF", "THEN", "ELSIF", "ELSE", "END_IF", "CASE", "OF", "END_CASE", "FOR", "TO", "BY", "DO", "END_FOR",
        "WHILE", "END_WHILE", "REPEAT", "UNTIL", "END_REPEAT", "EXIT", "CONTINUE", "RETURN", "AND", "OR", "XOR",
        "NOT", "MOD", "TRUE", "FALSE", "AND_THEN", "OR_ELSE", "JMP", "THIS", "SUPER", "REF", "ADR", "SIZEOF",
        "VAR", "END_VAR", "BEGIN", "REGION", "END_REGION"
    };

    /// <summary>
    /// Returns every tag use in the project's logic, in program/routine order.
    /// </summary>
    public IReadOnlyList<XrefEntry> Build(PlcProject project)
    {
        var entries = new List<XrefEntry>();
        var globalTags = project.Tags
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Scope, StringComparer.OrdinalIgnoreCase);

        foreach (var program in project.Programs)
        {
            var localTags = program.LocalTags.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            string? Resolve(string tag) =>
                localTags.Contains(tag) ? program.Name : globalTags.GetValueOrDefault(tag);

            foreach (var routine in program.Routines)
            {
                foreach (var rung in routine.Rungs)
                    AddInstructions(entries, rung.Instructions, program.Name, routine.Name, $"Rung {rung.Number}", Resolve);

                foreach (var network in routine.Networks)
                    AddInstructions(entries, network.Instructions, program.Name, routine.Name, $"Network {network.Number}", Resolve);

                AddSource(entries, routine.Source, program.Name, routine.Name, Resolve);
            }
        }

        foreach (var block in project.Blocks)
        {
            var interfaceNames = block.Interface.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            string? Resolve(string tag) =>
                interfaceNames.Contains(tag) ? block.Name : globalTags.GetValueOrDefault(tag);

            foreach (var network in block.Networks)
                AddInstructions(entries, network.Instructions, block.Name, block.Name, $"Network {network.Number}", Resolve);
            AddSource(entries, block.Source, block.Name, block.Name, Resolve);
        }

        return entries;
    }

    /// <summary>
    /// The base tag of an operand: "Motor" for "Motor.Run[2].0", "Local:1:I" for "Local:1:I.Data.3".
    /// Returns null for literals and placeholders.
    /// </summary>
    public static string? BaseTag(string operand)
    {
        var text = operand.Trim().TrimStart('#').Trim('"');
        if (text.Length == 0 || text == "?" || !(char.IsLetter(text[0]) || text[0] == '_'))
            return null;

        int end = 0;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] is '_' or ':'))
            end++;
        var name = text[..end];

        // Expressions (CPT/CMP) are not single operands.
        var rest = text[end..];
        if (rest.IndexOfAny(new[] { '+', '-', '*', '/', '(', ' ', '<', '>', '=' }) >= 0)
            return null;

        return name.Length == 0 ? null : name;
    }

    private static void AddInstructions(List<XrefEntry> entries, IReadOnlyList<InstructionInfo> instructions,
        string container, string routine, string position, Func<string, string?> resolve)
    {
        foreach (var instruction in instructions)
        {
            var written = WrittenOperands.GetValueOrDefault(instruction.Mnemonic) ?? Array.Empty<int>();
            for (int i = 0; i < instruction.Operands.Count; i++)
            {
                var operand = instruction.Operands[i];
                var baseTag = BaseTag(operand);
                if (baseTag != null)
                {
                    entries.Add(new XrefEntry
                    {
                        Tag = baseTag,
                        Operand = operand.Trim(),
                        Scope = resolve(baseTag),
                        Access = written.Contains(i) ? "Write" : "Read",
                        Container = container,
                        Routine = routine,
                        Position = position,
                        Instruction = instruction.Mnemonic
                    });
                    continue;
                }

                // Expression operands (CPT, CMP, FAL): every identifier in them is read.
                foreach (Match match in Identifier.Matches(operand))
                {
                    var name = match.Groups["name"].Value;
                    if (StKeywords.Contains(name) || resolve(name) == null)
                        continue;
                    entries.Add(new XrefEntry
                    {
                        Tag = name,
                        Operand = match.Value.TrimStart('#'),
                        Scope = resolve(name),
                        Access = "Read",
                        Container = container,
                        Routine = routine,
                        Position = position,
                        Instruction = instruction.Mnemonic
                    });
                }
            }
        }
    }

    private static void AddSource(List<XrefEntry> entries, string? source, string container, string routine,
        Func<string, string?> resolve)
    {
        if (string.IsNullOrEmpty(source))
            return;

        var lines = StripComments(source).Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var assignment = Assignment.Match(line);
            var targetStart = assignment.Success ? assignment.Groups["target"].Index : -1;

            foreach (Match match in Identifier.Matches(line))
            {
                var name = match.Groups["name"].Value;
                if (StKeywords.Contains(name))
                    continue;

                // In ST, unknown identifiers are mostly functions, types and enum values,
                // so only resolved tags are reported.
                var scope = resolve(name);
                if (scope == null)
                    continue;

                // Only the leading identifier of "target := ..." is written; index
                // expressions inside the target (e.g. a[i] := ...) are reads.
                var isTarget = match.Groups["name"].Index == targetStart;
                entries.Add(new XrefEntry
                {
                    Tag = name,
                    Operand = match.Value.TrimStart('#'),
                    Scope = scope,
                    Access = isTarget ? "Write" : "Read",
                    Container = container,
                    Routine = routine,
                    Position = $"Line {i + 1}",
                    Instruction = isTarget ? ":=" : ""
                });
            }
        }
    }

    /// <summary>
    /// Blanks out comments and string literals (keeping line breaks) so identifiers in them are ignored.
    /// </summary>
    internal static string StripComments(string source)
    {
        var chars = source.ToCharArray();
        int i = 0;
        while (i < chars.Length)
        {
            char c = chars[i];
            char next = i + 1 < chars.Length ? chars[i + 1] : '\0';
            int start = i;
            if (c == '/' && next == '/')
            {
                while (i < chars.Length && chars[i] != '\n') i++;
            }
            else if ((c == '(' || c == '/') && next == '*')
            {
                var close = c == '(' ? ")" : "/";
                i += 2;
                while (i < chars.Length && !(chars[i] == '*' && i + 1 < chars.Length && chars[i + 1] == close[0])) i++;
                i = Math.Min(i + 2, chars.Length);
            }
            else if (c is '\'' or '"')
            {
                i++;
                while (i < chars.Length && chars[i] != c)
                {
                    if (chars[i] == '$') i++;
                    i++;
                }
                i = Math.Min(i + 1, chars.Length);
            }
            else
            {
                i++;
                continue;
            }

            for (int k = start; k < i && k < chars.Length; k++)
                if (chars[k] != '\n') chars[k] = ' ';
        }
        return new string(chars);
    }
}
