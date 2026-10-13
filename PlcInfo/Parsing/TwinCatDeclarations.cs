using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PlcInfo.Models;

namespace PlcInfo.Parsing;

/// <summary>
/// IEC 61131-3 declaration handling for TwinCAT objects: comment/pragma stripping,
/// VAR sections, POU headers and DUT (TYPE ... END_TYPE) declarations.
/// </summary>
public partial class TwinCatParser
{
    /// <summary>One ';'-terminated declaration inside a VAR, STRUCT or UNION section.</summary>
    internal sealed record DeclarationStatement(string Text, string Section, IReadOnlyList<string> Qualifiers, string? Comment);

    /// <summary>The header of a POU, method or property declaration (the text before the first VAR section).</summary>
    internal sealed record PouHeader(
        string? Keyword,
        string? Name,
        IReadOnlyList<string> Modifiers,
        string? Extends,
        IReadOnlyList<string> Implements,
        string? ReturnType);

    private static readonly HashSet<string> SectionKeywords = new(StringComparer.Ordinal)
    {
        "VAR", "VAR_INPUT", "VAR_OUTPUT", "VAR_IN_OUT", "VAR_GLOBAL", "VAR_TEMP", "VAR_STAT",
        "VAR_INST", "VAR_EXTERNAL", "VAR_CONFIG", "VAR_ACCESS"
    };

    private static readonly HashSet<string> SectionQualifiers = new(StringComparer.Ordinal)
    {
        "CONSTANT", "RETAIN", "PERSISTENT", "NON_RETAIN"
    };

    private static readonly HashSet<string> AccessModifiers = new(StringComparer.Ordinal)
    {
        "PUBLIC", "PRIVATE", "PROTECTED", "INTERNAL", "ABSTRACT", "FINAL"
    };

    private static readonly Regex HeaderEnd = new(
        @"\b(VAR(_INPUT|_OUTPUT|_IN_OUT|_GLOBAL|_TEMP|_STAT|_INST|_EXTERNAL|_CONFIG|_ACCESS)?|STRUCT|UNION|END_TYPE)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex TypeHeader = new(
        @"\bTYPE\s+(?<name>[A-Za-z_][\w.]*)\s*(?:EXTENDS\s+(?<ext>[\w.]+(?:\s*,\s*[\w.]+)*))?\s*:(?<body>.*?)\bEND_TYPE\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Resolves a project-relative reference (TwinCAT writes Windows separators) and
    /// rejects anything that escapes the project directory, such as "../x" or rooted paths.
    /// </summary>
    internal static string? ResolveProjectReference(string projectDir, string reference) =>
        ResolveProjectReference(projectDir, projectDir, reference);

    /// <summary>
    /// Resolves <paramref name="reference"/> relative to <paramref name="baseDir"/> and returns null
    /// unless the result stays inside <paramref name="rootDir"/>. An .xti file under _Config/PLC/
    /// refers to its .plcproj as "..\..\X\X.plcproj", so the base and the root can differ.
    /// </summary>
    internal static string? ResolveProjectReference(string rootDir, string baseDir, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        var normalized = reference.Trim().Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
            return null;

        var root = Path.GetFullPath(rootDir);
        var full = Path.GetFullPath(Path.Combine(Path.GetFullPath(baseDir), normalized));
        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return full.StartsWith(rootWithSep, StringComparison.Ordinal) ? full : null;
    }

    /// <summary>
    /// Parses the VAR sections of a declaration into tags. Comments and pragmas are ignored,
    /// except that a comment on the line that ends a declaration becomes its description.
    /// </summary>
    internal static List<TagInfo> ParseDeclarationText(string declarationText, string scope)
    {
        var tags = new List<TagInfo>();
        foreach (var statement in ScanDeclarationStatements(declarationText, dataTypeMode: false))
            tags.AddRange(ParseDeclarationStatement(statement, scope));
        return tags;
    }

    private static IEnumerable<TagInfo> ParseDeclarationStatement(DeclarationStatement statement, string scope)
    {
        var text = statement.Text;
        var colonIndex = IndexOutsideStrings(text, ':', skipAssign: true);
        if (colonIndex <= 0)
            yield break;

        var namePart = text[..colonIndex].Trim();
        var typePart = text[(colonIndex + 1)..].Trim();

        string? initialValue = null;
        var assignIndex = IndexOfAssign(typePart);
        if (assignIndex >= 0)
        {
            initialValue = typePart[(assignIndex + 2)..].Trim();
            typePart = typePart[..assignIndex].Trim();
        }
        typePart = Whitespace.Replace(typePart, " ");

        // Direct address: "bIn AT %IX0.0 : BOOL"
        string? address = null;
        var atMatch = Regex.Match(namePart, @"\s+AT\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (atMatch.Success)
        {
            address = namePart[(atMatch.Index + atMatch.Length)..].Trim();
            namePart = namePart[..atMatch.Index].Trim();
        }

        var persistent = statement.Qualifiers.Contains("PERSISTENT");
        var usage = statement.Section switch
        {
            "VAR_INPUT" => "Input",
            "VAR_OUTPUT" => "Output",
            "VAR_IN_OUT" => "InOut",
            "VAR" when persistent => "VAR_PERSISTENT",
            _ => statement.Section
        };

        // "a, b, c : BOOL;" declares three variables.
        foreach (var name in namePart.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var tag = new TagInfo
            {
                Name = name,
                DataType = typePart,
                Scope = scope,
                Usage = usage,
                Description = statement.Comment,
                InitialValue = initialValue,
                Address = address,
                IsIoTag = address?.StartsWith("%I", StringComparison.OrdinalIgnoreCase) == true ||
                          address?.StartsWith("%Q", StringComparison.OrdinalIgnoreCase) == true
            };
            foreach (var qualifier in statement.Qualifiers)
                tag.Properties[QualifierPropertyName(qualifier)] = "true";
            yield return tag;
        }
    }

    private static string QualifierPropertyName(string qualifier) => qualifier switch
    {
        "CONSTANT" => "Constant",
        "RETAIN" => "Retain",
        "PERSISTENT" => "Persistent",
        "NON_RETAIN" => "NonRetain",
        _ => qualifier
    };

    /// <summary>
    /// Splits a declaration into ';'-terminated statements, tracking the enclosing section.
    /// In <paramref name="dataTypeMode"/> STRUCT and UNION open a section as well (DUT bodies).
    /// Section keywords are recognised anywhere outside strings, so one-line forms such as
    /// "VAR_INPUT a : INT; END_VAR" work too.
    /// </summary>
    internal static List<DeclarationStatement> ScanDeclarationStatements(string text, bool dataTypeMode)
    {
        var lineComments = new Dictionary<int, List<string>>();
        var cleaned = StripCommentsAndPragmas(text, lineComments);
        var result = new List<DeclarationStatement>();
        var section = "";
        IReadOnlyList<string> qualifiers = Array.Empty<string>();
        var buffer = new StringBuilder();
        var line = 0;
        var i = 0;

        while (i < cleaned.Length)
        {
            var c = cleaned[i];
            if (c == '\n')
            {
                line++;
                if (section.Length > 0) buffer.Append(' ');
                i++;
                continue;
            }

            if (c == '\'' || c == '"')
            {
                var end = SkipStringLiteral(cleaned, i);
                if (section.Length > 0) buffer.Append(cleaned, i, end - i);
                i = end;
                continue;
            }

            if (IsIdentifierStart(c) && (i == 0 || !IsIdentifierChar(cleaned[i - 1]) && cleaned[i - 1] != '.' && cleaned[i - 1] != '#'))
            {
                var j = i;
                while (j < cleaned.Length && IsIdentifierChar(cleaned[j])) j++;
                var word = cleaned[i..j];
                var upper = word.ToUpperInvariant();

                if (SectionKeywords.Contains(upper) || dataTypeMode && (upper == "STRUCT" || upper == "UNION"))
                {
                    section = upper;
                    buffer.Clear();
                    var quals = new List<string>();
                    // Qualifiers apply to the whole section: "VAR_GLOBAL CONSTANT", "VAR PERSISTENT RETAIN".
                    while (true)
                    {
                        var k = j;
                        var newlines = 0;
                        while (k < cleaned.Length && char.IsWhiteSpace(cleaned[k]))
                        {
                            if (cleaned[k] == '\n') newlines++;
                            k++;
                        }
                        var m = k;
                        while (m < cleaned.Length && IsIdentifierChar(cleaned[m])) m++;
                        var next = cleaned[k..m].ToUpperInvariant();
                        if (m == k || !SectionQualifiers.Contains(next))
                            break;
                        quals.Add(next);
                        line += newlines;
                        j = m;
                    }
                    qualifiers = quals;
                    i = j;
                    continue;
                }

                if (upper is "END_VAR" or "END_STRUCT" or "END_UNION")
                {
                    section = "";
                    buffer.Clear();
                    i = j;
                    continue;
                }

                if (section.Length > 0) buffer.Append(word);
                i = j;
                continue;
            }

            if (c == ';')
            {
                if (section.Length > 0)
                {
                    var statement = buffer.ToString().Trim();
                    if (statement.Length > 0)
                    {
                        var comment = lineComments.TryGetValue(line, out var comments) ? string.Join(" ", comments) : null;
                        result.Add(new DeclarationStatement(statement, section, qualifiers, comment));
                    }
                }
                buffer.Clear();
                i++;
                continue;
            }

            if (section.Length > 0) buffer.Append(c);
            i++;
        }

        return result;
    }

    /// <summary>
    /// Removes (* *), /* */ and // comments and {pragmas} that are outside string literals,
    /// keeping newlines so line structure survives. Block comments may nest, as TwinCAT allows.
    /// </summary>
    internal static string StripCommentsAndPragmas(string text) => StripCommentsAndPragmas(text, null);

    private static string StripCommentsAndPragmas(string text, Dictionary<int, List<string>>? lineComments)
    {
        var sb = new StringBuilder(text.Length);
        var line = 0;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (c == '\'' || c == '"')
            {
                var end = SkipStringLiteral(text, i);
                for (var k = i; k < end; k++)
                    if (text[k] == '\n') line++;
                sb.Append(text, i, end - i);
                i = end;
                continue;
            }

            if (c == '(' && next == '*' || c == '/' && next == '*')
            {
                var open = c == '(' ? "(*" : "/*";
                var close = c == '(' ? "*)" : "*/";
                var stop = FindBlockCommentEnd(text, i, open, close);
                RecordComment(lineComments, line, text[(i + 2)..Math.Max(i + 2, stop - 2)]);
                line += AppendNewlines(sb, text, i, stop);
                i = stop;
                continue;
            }

            if (c == '/' && next == '/')
            {
                var start = i;
                while (i < text.Length && text[i] != '\n') i++;
                RecordComment(lineComments, line, text[(start + 2)..i]);
                continue;
            }

            if (c == '{')
            {
                var end = text.IndexOf('}', i + 1);
                var stop = end < 0 ? text.Length : end + 1;
                line += AppendNewlines(sb, text, i, stop);
                i = stop;
                continue;
            }

            if (c == '\n') line++;
            sb.Append(c);
            i++;
        }
        return sb.ToString();

        static int AppendNewlines(StringBuilder sb, string text, int from, int to)
        {
            var count = 0;
            for (var k = from; k < to; k++)
            {
                if (text[k] != '\n') continue;
                sb.Append('\n');
                count++;
            }
            return count;
        }
    }

    private static void RecordComment(Dictionary<int, List<string>>? lineComments, int line, string comment)
    {
        if (lineComments == null) return;
        var cleaned = Whitespace.Replace(comment.Trim().Trim('*').Trim(), " ");
        if (cleaned.Length == 0) return;
        if (!lineComments.TryGetValue(line, out var list))
            lineComments[line] = list = new List<string>();
        list.Add(cleaned);
    }

    private static int FindBlockCommentEnd(string text, int start, string open, string close)
    {
        var depth = 0;
        var i = start;
        while (i < text.Length - 1)
        {
            if (string.CompareOrdinal(text, i, open, 0, 2) == 0) { depth++; i += 2; continue; }
            if (string.CompareOrdinal(text, i, close, 0, 2) == 0)
            {
                depth--;
                i += 2;
                if (depth == 0) return i;
                continue;
            }
            i++;
        }
        return text.Length;
    }

    // Returns the index just past the literal that starts at 'start' ($-escapes keep us inside it).
    private static int SkipStringLiteral(string text, int start)
    {
        var quote = text[start];
        var i = start + 1;
        while (i < text.Length)
        {
            var c = text[i++];
            if (c == '$' && i < text.Length) { i++; continue; }
            if (c == quote) break;
        }
        return i;
    }

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static int IndexOutsideStrings(string text, char target, bool skipAssign, int start = 0)
    {
        char quote = '\0';
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (c == '$') { i++; continue; }
                if (c == quote) quote = '\0';
                continue;
            }
            if (c == '\'' || c == '"') { quote = c; continue; }
            if (c == target)
            {
                if (skipAssign && i + 1 < text.Length && text[i + 1] == '=')
                    continue;
                return i;
            }
        }
        return -1;
    }

    private static int IndexOfAssign(string text)
    {
        var i = IndexOutsideStrings(text, ':', skipAssign: false);
        while (i >= 0)
        {
            if (i + 1 < text.Length && text[i + 1] == '=')
                return i;
            i = IndexOutsideStrings(text, ':', skipAssign: false, i + 1);
        }
        return -1;
    }

    /// <summary>
    /// Parses the header of a POU, method or property declaration, e.g.
    /// "FUNCTION_BLOCK PUBLIC FB_X EXTENDS FB_Base IMPLEMENTS I_A, I_B" or "METHOD Foo : BOOL".
    /// Returns a header with a null keyword when the text has none (GVLs).
    /// </summary>
    internal static PouHeader ParsePouHeader(string declaration)
    {
        var cleaned = StripCommentsAndPragmas(declaration);
        var end = HeaderEnd.Match(cleaned);
        var header = Whitespace.Replace(end.Success ? cleaned[..end.Index] : cleaned, " ").Trim().TrimEnd(';').Trim();
        if (header.Length == 0)
            return new PouHeader(null, null, Array.Empty<string>(), null, Array.Empty<string>(), null);

        string? returnType = null;
        var colon = IndexOutsideStrings(header, ':', skipAssign: true);
        if (colon >= 0)
        {
            returnType = header[(colon + 1)..].Trim();
            header = header[..colon].Trim();
            if (returnType.Length == 0) returnType = null;
        }

        string? extends = null;
        var implements = new List<string>();
        var implementsMatch = Regex.Match(header, @"\bIMPLEMENTS\s+(?<list>[\w.]+(?:\s*,\s*[\w.]+)*)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (implementsMatch.Success)
        {
            implements.AddRange(implementsMatch.Groups["list"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            header = header.Remove(implementsMatch.Index, implementsMatch.Length);
        }
        var extendsMatch = Regex.Match(header, @"\bEXTENDS\s+(?<list>[\w.]+(?:\s*,\s*[\w.]+)*)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (extendsMatch.Success)
        {
            extends = Whitespace.Replace(extendsMatch.Groups["list"].Value, "");
            header = header.Remove(extendsMatch.Index, extendsMatch.Length);
        }

        var words = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var keyword = words.Length > 0 ? words[0].ToUpperInvariant() : null;
        var modifiers = new List<string>();
        string? name = null;
        foreach (var word in words.Skip(1))
        {
            var upper = word.ToUpperInvariant();
            if (name == null && AccessModifiers.Contains(upper))
                modifiers.Add(upper);
            else
                name ??= word;
        }

        return new PouHeader(keyword, name, modifiers, extends, implements, returnType);
    }

    /// <summary>Maps a POU keyword to the vendor-neutral program type.</summary>
    internal static string? MapPouKind(string? keyword) => keyword switch
    {
        "PROGRAM" => "Program",
        "FUNCTION_BLOCK" => "FunctionBlock",
        "FUNCTION" => "Function",
        "INTERFACE" => "Interface",
        _ => null
    };

    /// <summary>
    /// Parses a DUT declaration ("TYPE X : STRUCT ... END_STRUCT END_TYPE", an enumeration
    /// "(A, B := 5) INT", "UNION ... END_UNION", or an alias "STRING(80)").
    /// Returns null when the text has no TYPE ... END_TYPE block.
    /// </summary>
    internal static DataTypeInfo? ParseDutDeclaration(string declaration)
    {
        var cleaned = StripCommentsAndPragmas(declaration);
        var match = TypeHeader.Match(cleaned);
        if (!match.Success)
            return null;

        var name = match.Groups["name"].Value;
        var extends = match.Groups["ext"].Success ? Whitespace.Replace(match.Groups["ext"].Value, "") : null;
        var body = match.Groups["body"].Value.Trim();
        var upperBody = body.ToUpperInvariant();

        if (Regex.IsMatch(upperBody, @"^(STRUCT|UNION)\b"))
        {
            var isUnion = upperBody.StartsWith("UNION", StringComparison.Ordinal);
            var members = ScanDeclarationStatements(declaration, dataTypeMode: true)
                .Where(s => s.Section is "STRUCT" or "UNION")
                .SelectMany(s => ParseDeclarationStatement(s, name))
                .Select(ToMember)
                .ToList();
            return new DataTypeInfo
            {
                Name = name,
                Kind = isUnion ? "Union" : "Struct",
                BaseType = extends,
                Members = members
            };
        }

        if (body.StartsWith('('))
            return ParseEnum(name, body);

        return new DataTypeInfo
        {
            Name = name,
            Kind = "Alias",
            BaseType = Whitespace.Replace(body.TrimEnd().TrimEnd(';').Trim(), " ")
        };
    }

    private static DataTypeMember ToMember(TagInfo tag)
    {
        var member = new DataTypeMember
        {
            Name = tag.Name,
            DataType = tag.DataType,
            Description = tag.Description,
            InitialValue = tag.InitialValue
        };

        var array = Regex.Match(tag.DataType, @"^ARRAY\s*\[(?<dims>[^\]]*)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (array.Success)
            member.Dimensions = Whitespace.Replace(array.Groups["dims"].Value, "");
        if (!string.IsNullOrEmpty(tag.Address))
            member.Properties["Address"] = tag.Address;
        return member;
    }

    private static DataTypeInfo ParseEnum(string name, string body)
    {
        var close = FindMatchingParen(body, 0);
        var inner = close > 0 ? body[1..close] : body[1..];
        var after = close > 0 ? body[(close + 1)..].Trim().TrimEnd(';').Trim() : "";
        // An enumeration without an explicit base type is INT in TwinCAT/CODESYS.
        var baseType = after.Length > 0 ? Whitespace.Replace(after, " ") : null;

        var members = new List<DataTypeMember>();
        long? previous = null;
        foreach (var item in SplitTopLevel(inner, ','))
        {
            var entry = item.Trim();
            if (entry.Length == 0) continue;

            string memberName;
            string? value;
            var assign = entry.IndexOf(":=", StringComparison.Ordinal);
            if (assign >= 0)
            {
                memberName = entry[..assign].Trim();
                value = entry[(assign + 2)..].Trim();
                previous = TryParseIecInteger(value);
            }
            else
            {
                memberName = entry;
                // Implicit values continue from the previous one (the first defaults to 0).
                previous = members.Count == 0 ? 0 : previous + 1;
                value = previous?.ToString(CultureInfo.InvariantCulture);
            }

            members.Add(new DataTypeMember { Name = memberName, DataType = baseType ?? "INT", InitialValue = value });
        }

        return new DataTypeInfo { Name = name, Kind = "Enum", BaseType = baseType, Members = members };
    }

    private static int FindMatchingParen(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '\'' || text[i] == '"') { i = SkipStringLiteral(text, i) - 1; continue; }
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0) return i;
        }
        return -1;
    }

    private static IEnumerable<string> SplitTopLevel(string text, char separator)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\'' || c == '"') { i = SkipStringLiteral(text, i) - 1; continue; }
            if (c is '(' or '[') depth++;
            else if (c is ')' or ']') depth--;
            else if (c == separator && depth == 0)
            {
                yield return text[start..i];
                start = i + 1;
            }
        }
        yield return text[start..];
    }

    /// <summary>Parses IEC integer literals such as 5, -3, 1_000, 16#FF and UDINT#16#FF.</summary>
    internal static long? TryParseIecInteger(string literal)
    {
        var text = literal.Replace("_", "").Trim();
        var hashes = text.Split('#');
        // Drop a type prefix such as "UDINT#".
        if (hashes.Length == 3 || hashes.Length == 2 && !int.TryParse(hashes[0], NumberStyles.None, CultureInfo.InvariantCulture, out _))
            hashes = hashes[1..];

        try
        {
            if (hashes.Length == 2 && int.TryParse(hashes[0], NumberStyles.None, CultureInfo.InvariantCulture, out var radix) &&
                radix is 2 or 8 or 16)
                return (long)Convert.ToUInt64(hashes[1], radix);
            if (hashes.Length == 1 && long.TryParse(hashes[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
                return value;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
        }
        return null;
    }
}
