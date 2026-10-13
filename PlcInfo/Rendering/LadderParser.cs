namespace PlcInfo.Rendering;

/// <summary>A node of a ladder rung: an instruction, a series of nodes, or a parallel branch.</summary>
public abstract record LadderNode;

/// <summary>One instruction, e.g. XIC(Start) or TON(Timer1,1000,0).</summary>
public sealed record LadderInstruction(string Mnemonic, IReadOnlyList<string> Operands) : LadderNode
{
    /// <summary>Stable key used for highlighting, e.g. "OTE(Motor)".</summary>
    public string Key => $"{Mnemonic}({string.Join(",", Operands)})";
}

/// <summary>Nodes connected in series, left to right.</summary>
public sealed record LadderSeries(IReadOnlyList<LadderNode> Items) : LadderNode;

/// <summary>Parallel legs between two branch points. A leg may be empty (a plain wire).</summary>
public sealed record LadderBranch(IReadOnlyList<LadderSeries> Legs) : LadderNode;

/// <summary>
/// Parses ladder rung text into a tree. Accepts Logix neutral text
/// (<c>XIC(a)[XIC(b),XIO(c)]OTE(d);</c>) and the PLCopen LD canonical text
/// produced by plcinfo (<c>XIC(a) [XIC(b) , XIO(c)] OTE(d)</c>).
/// </summary>
public static class LadderParser
{
    /// <summary>
    /// Parses <paramref name="text"/>, or returns null if it is not ladder text
    /// (e.g. a Siemens boolean expression or ST).
    /// </summary>
    public static LadderSeries? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        int position = 0;
        try
        {
            var series = ParseSeries(text, ref position, topLevel: true);
            SkipWhitespace(text, ref position);
            if (position < text.Length && text[position] == ';')
                position++;
            SkipWhitespace(text, ref position);
            return position == text.Length && series.Items.Count > 0 ? series : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static LadderSeries ParseSeries(string text, ref int position, bool topLevel)
    {
        var items = new List<LadderNode>();
        while (true)
        {
            SkipWhitespace(text, ref position);
            if (position >= text.Length)
                break;

            var c = text[position];
            if (c == '[')
            {
                position++;
                items.Add(ParseBranch(text, ref position));
            }
            else if (char.IsLetter(c) || c == '_')
            {
                items.Add(ParseInstruction(text, ref position));
            }
            else if (c is ',' or ']')
            {
                if (topLevel)
                    throw new FormatException("Unexpected branch delimiter at top level.");
                break;
            }
            else if (c == ';')
            {
                break;
            }
            else
            {
                throw new FormatException($"Unexpected '{c}'.");
            }
        }
        return new LadderSeries(items);
    }

    private static LadderBranch ParseBranch(string text, ref int position)
    {
        var legs = new List<LadderSeries>();
        while (true)
        {
            legs.Add(ParseSeries(text, ref position, topLevel: false));
            SkipWhitespace(text, ref position);
            if (position >= text.Length)
                throw new FormatException("Unclosed branch.");
            if (text[position] == ',')
            {
                position++;
                continue;
            }
            if (text[position] == ']')
            {
                position++;
                return new LadderBranch(legs);
            }
            throw new FormatException("Expected ',' or ']'.");
        }
    }

    private static LadderInstruction ParseInstruction(string text, ref int position)
    {
        int start = position;
        while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] == '_'))
            position++;
        var mnemonic = text[start..position];

        SkipWhitespace(text, ref position);
        if (position >= text.Length || text[position] != '(')
            throw new FormatException($"Instruction '{mnemonic}' has no operand list.");
        position++;

        var operands = new List<string>();
        var current = new System.Text.StringBuilder();
        int depth = 0;
        while (true)
        {
            if (position >= text.Length)
                throw new FormatException("Unclosed operand list.");
            var c = text[position++];
            if (c is '(' or '[')
            {
                depth++;
            }
            else if (c is ')' or ']')
            {
                if (c == ')' && depth == 0)
                    break;
                if (depth > 0)
                    depth--;
            }
            else if (c == ',' && depth == 0)
            {
                operands.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }
            current.Append(c);
        }

        var last = current.ToString().Trim();
        if (last.Length > 0 || operands.Count > 0)
            operands.Add(last);
        return new LadderInstruction(mnemonic, operands);
    }

    private static void SkipWhitespace(string text, ref int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
            position++;
    }
}
