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

    // Siemens box instructions (TON, CTU, MOVE, ...) take their operands in a different
    // order than Rockwell's, so for Siemens projects only coils count as writes.
    private static readonly Dictionary<string, int[]> SiemensWrittenOperands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["="] = new[] { 0 }, ["S"] = new[] { 0 }, ["R"] = new[] { 0 },
        ["Coil"] = new[] { 0 }, ["SCoil"] = new[] { 0 }, ["RCoil"] = new[] { 0 }
    };

    // A reference: optional #, a plain or "quoted" first segment, then member/index accessors.
    // THIS^./SUPER^. prefixes are matched separately (they make the member a local).
    private static readonly Regex Reference = new(
        @"(?<![\w.#%^""])(?<self>(?:THIS|SUPER)\^\.)?#?(?:(?<name>[A-Za-z_][A-Za-z0-9_:]*)|""(?<name>[^""\n]+)"")(?<rest>(?:\.(?:[A-Za-z0-9_]+|""[^""\n]+"")|\[[^\]]*\])*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex Member = new(@"^\.(?:(?<m>[A-Za-z0-9_]+)|""(?<m>[^""]+)"")", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Assignment = new(@"^\s*(?:(?:THIS|SUPER)\^\.)?#?(?<target>[A-Za-z_][A-Za-z0-9_]*|""[^""]+"")[A-Za-z0-9_.\[\],""]*\s*(:=|\?=|S=|R=)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> StKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "IF", "THEN", "ELSIF", "ELSE", "END_IF", "CASE", "OF", "END_CASE", "FOR", "TO", "BY", "DO", "END_FOR",
        "WHILE", "END_WHILE", "REPEAT", "UNTIL", "END_REPEAT", "EXIT", "CONTINUE", "RETURN", "AND", "OR", "XOR",
        "NOT", "MOD", "TRUE", "FALSE", "AND_THEN", "OR_ELSE", "JMP", "THIS", "SUPER", "REF", "ADR", "SIZEOF",
        "VAR", "END_VAR", "BEGIN", "REGION", "END_REGION"
    };

    /// <summary>
    /// Resolves names to tags for one container (program, POU, AOI or block):
    /// its own tags, tags inherited through EXTENDS, then globals; and qualified
    /// access to a GVL/DB member ("GVL.x", "DB".x).
    /// </summary>
    private sealed class Resolver
    {
        private readonly string _container;
        private readonly HashSet<string> _locals;
        private readonly IReadOnlyDictionary<string, string> _globals;
        private readonly IReadOnlyDictionary<string, HashSet<string>> _scopes;

        public Resolver(string container, HashSet<string> locals,
            IReadOnlyDictionary<string, string> globals, IReadOnlyDictionary<string, HashSet<string>> scopes)
        {
            _container = container;
            _locals = locals;
            _globals = globals;
            _scopes = scopes;
        }

        /// <summary>
        /// Resolves a reference to (tag, scope), or null when it is not a known tag.
        /// </summary>
        public (string Tag, string Scope)? Resolve(string name, string rest, bool self)
        {
            if (self || _locals.Contains(name))
                return _locals.Contains(name) ? (name, _container) : null;

            if (_globals.TryGetValue(name, out var scope))
                return (name, scope);

            // Qualified access: the first segment names a GVL or DB. DB struct members can be
            // stored with dotted paths ("General.EmergencyStop"), so take the longest match.
            if (_scopes.TryGetValue(name, out var members))
            {
                string? best = null;
                var path = "";
                var remaining = rest;
                Match member;
                while ((member = Member.Match(remaining)).Success)
                {
                    path = path.Length == 0 ? member.Groups["m"].Value : $"{path}.{member.Groups["m"].Value}";
                    if (members.Contains(path))
                        best = path;
                    remaining = remaining[member.Length..];
                    while (remaining.StartsWith('['))
                        remaining = remaining[(remaining.IndexOf(']') + 1)..];
                }
                if (best != null)
                    return (best, name);
            }

            return null;
        }
    }

    /// <summary>
    /// Returns every tag use in the project's logic, in program/routine order.
    /// </summary>
    public IReadOnlyList<XrefEntry> Build(PlcProject project)
    {
        var entries = new List<XrefEntry>();

        // Unqualified globals: controller/global-list tags. Tags in a DB or GVL are also
        // reachable qualified, via the scope table.
        var globals = project.Tags
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Scope, StringComparer.OrdinalIgnoreCase);
        var scopes = project.Tags
            .GroupBy(t => t.Scope, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var programsByName = project.Programs
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var siemens = project.Vendor.Contains("Siemens", StringComparison.OrdinalIgnoreCase);
        var writes = siemens ? SiemensWrittenOperands : WrittenOperands;

        foreach (var program in project.Programs)
        {
            // Members inherited through EXTENDS are visible in a derived POU; they are
            // reported against the POU that declares them.
            var locals = program.LocalTags.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var resolvers = new List<Resolver> { new(program.Name, locals, globals, scopes) };
            foreach (var baseProgram in BaseChain(program, programsByName))
            {
                resolvers.Add(new Resolver(baseProgram.Name,
                    baseProgram.LocalTags.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, string>(), new Dictionary<string, HashSet<string>>()));
            }
            (string, string)? Resolve(string name, string rest, bool self) =>
                resolvers.Select(r => r.Resolve(name, rest, self)).FirstOrDefault(r => r != null);

            foreach (var routine in program.Routines)
            {
                foreach (var rung in routine.Rungs)
                    AddInstructions(entries, rung.Instructions, program.Name, routine.Name, $"Rung {rung.Number}", Resolve, writes);

                foreach (var network in routine.Networks)
                    AddInstructions(entries, network.Instructions, program.Name, routine.Name, $"Network {network.Number}", Resolve, writes);

                AddSource(entries, routine.Source, program.Name, routine.Name, Resolve, quotedIdentifiers: siemens);
            }
        }

        foreach (var block in project.Blocks)
        {
            // Interface keys are "Section.Name" (Siemens) or plain names.
            var locals = block.Interface.Keys
                .Select(k => k.Contains('.') ? k[(k.IndexOf('.') + 1)..] : k)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var resolver = new Resolver(block.Name, locals, globals, scopes);
            (string, string)? Resolve(string name, string rest, bool self) => resolver.Resolve(name, rest, self);

            foreach (var network in block.Networks)
                AddInstructions(entries, network.Instructions, block.Name, block.Name, $"Network {network.Number}", Resolve, writes);
            AddSource(entries, block.Source, block.Name, block.Name, Resolve, quotedIdentifiers: siemens);
        }

        return entries;
    }

    private static IEnumerable<ProgramInfo> BaseChain(ProgramInfo program, IReadOnlyDictionary<string, ProgramInfo> programsByName)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { program.Name };
        var current = program;
        while (current.Properties.TryGetValue("Extends", out var extends) && !string.IsNullOrWhiteSpace(extends))
        {
            // CODESYS can list several (interfaces use EXTENDS too); follow each that is a POU.
            var next = extends.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(n => programsByName.GetValueOrDefault(n))
                .FirstOrDefault(p => p != null && seen.Add(p.Name));
            if (next == null)
                yield break;
            yield return next;
            current = next;
        }
    }

    /// <summary>
    /// The base tag of an operand: "Motor" for "Motor.Run[2].0", "Local:1:I" for "Local:1:I.Data.3",
    /// "DB_Motors" for "\"DB_Motors\".Speed". Returns null for literals, placeholders and expressions.
    /// </summary>
    public static string? BaseTag(string operand)
    {
        var text = operand.Trim();
        var match = Reference.Match(text);
        if (!match.Success || match.Index != 0 || match.Length != text.Length)
            return null;
        return match.Groups["name"].Value;
    }

    private static void AddInstructions(List<XrefEntry> entries, IReadOnlyList<InstructionInfo> instructions,
        string container, string routine, string position, Func<string, string, bool, (string Tag, string Scope)?> resolve,
        IReadOnlyDictionary<string, int[]> writes)
    {
        foreach (var instruction in instructions)
        {
            var written = writes.GetValueOrDefault(instruction.Mnemonic) ?? Array.Empty<int>();
            for (int i = 0; i < instruction.Operands.Count; i++)
            {
                var operand = instruction.Operands[i].Trim();
                var whole = Reference.Match(operand);
                if (whole.Success && whole.Index == 0 && whole.Length == operand.Length)
                {
                    var name = whole.Groups["name"].Value;
                    var resolved = resolve(name, whole.Groups["rest"].Value, whole.Groups["self"].Success);
                    entries.Add(new XrefEntry
                    {
                        Tag = resolved?.Tag ?? name,
                        Operand = operand,
                        Scope = resolved?.Scope,
                        Access = written.Contains(i) ? "Write" : "Read",
                        Container = container,
                        Routine = routine,
                        Position = position,
                        Instruction = instruction.Mnemonic
                    });
                    continue;
                }

                // Expression operands (CPT, CMP, FAL): every known tag in them is read.
                foreach (Match match in Reference.Matches(operand))
                {
                    var name = match.Groups["name"].Value;
                    if (StKeywords.Contains(name))
                        continue;
                    var resolved = resolve(name, match.Groups["rest"].Value, match.Groups["self"].Success);
                    if (resolved == null)
                        continue;
                    entries.Add(new XrefEntry
                    {
                        Tag = resolved.Value.Tag,
                        Operand = match.Value,
                        Scope = resolved.Value.Scope,
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
        Func<string, string, bool, (string Tag, string Scope)?> resolve, bool quotedIdentifiers)
    {
        if (string.IsNullOrEmpty(source))
            return;

        var lines = StripComments(source, quotedIdentifiers).Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var assignment = Assignment.Match(line);
            var targetStart = assignment.Success ? assignment.Groups["target"].Index : -1;

            foreach (Match match in Reference.Matches(line))
            {
                var name = match.Groups["name"].Value;
                if (StKeywords.Contains(name))
                    continue;

                // In ST, unknown identifiers are mostly functions, types and enum values,
                // so only resolved tags are reported.
                var resolved = resolve(name, match.Groups["rest"].Value, match.Groups["self"].Success);
                if (resolved == null)
                    continue;

                // Only the leading reference of "target := ..." is written; index
                // expressions inside the target (e.g. a[i] := ...) are reads.
                var nameGroup = match.Groups["name"];
                var isTarget = nameGroup.Index == targetStart || nameGroup.Index - 1 == targetStart;
                entries.Add(new XrefEntry
                {
                    Tag = resolved.Value.Tag,
                    Operand = match.Value,
                    Scope = resolved.Value.Scope,
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
    /// In Siemens SCL, double quotes delimit names (global tags, DBs), not strings, so they are kept
    /// when <paramref name="quotedIdentifiers"/> is set.
    /// </summary>
    internal static string StripComments(string source, bool quotedIdentifiers = false)
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
                var close = c == '(' ? ')' : '/';
                i += 2;
                while (i < chars.Length && !(chars[i] == '*' && i + 1 < chars.Length && chars[i + 1] == close)) i++;
                i = Math.Min(i + 2, chars.Length);
            }
            else if (c == '\'' || (c == '"' && !quotedIdentifiers))
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
