using System.Globalization;
using System.Net;
using System.Text;

namespace PlcInfo.Rendering;

/// <summary>
/// Draws a ladder rung as inline SVG: power rails, contacts (-| |-, -|/|-), coils
/// (-( )-, (L), (U)), boxes for other instructions, and parallel branches.
/// Styling comes from CSS classes (lad-wire, lad-sym, lad-text, lad-box, lad-add, lad-del)
/// so the drawing follows the page theme. All text is XML-encoded.
/// </summary>
public static class LadderSvgRenderer
{
    private const int WireY = 30;          // wire position from the top of each element
    private const int CellHeight = 50;     // height of a contact or coil cell
    private const int SymbolWidth = 110;   // width of a contact or coil cell (fits MaxLabel at 11px)
    private const int BranchPad = 14;      // horizontal space for branch connections
    private const int RailMargin = 10;
    private const int MaxLabel = 15;       // characters shown above a contact or coil; full name in the tooltip

    private static readonly HashSet<string> Contacts = new(StringComparer.OrdinalIgnoreCase) { "XIC", "XIO", "XICP", "XICN" };
    private static readonly HashSet<string> Coils = new(StringComparer.OrdinalIgnoreCase) { "OTE", "OTL", "OTU", "OTN" };

    /// <summary>
    /// Renders <paramref name="rung"/> as an SVG element. <paramref name="highlight"/> may return
    /// a CSS class (e.g. "lad-add") for instructions that should be emphasized.
    /// </summary>
    public static string Render(LadderSeries rung, Func<LadderInstruction, string?>? highlight = null)
    {
        var (width, height) = Measure(rung);
        var total = width + 2 * RailMargin + 40;
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"<svg class=\"ladder\" xmlns=\"http://www.w3.org/2000/svg\" width=\"{total}\" height=\"{height}\" viewBox=\"0 0 {total} {height}\" role=\"img\">");

        // Power rails and the wire from the last element to the right rail.
        Line(sb, RailMargin, 0, RailMargin, height, "lad-rail");
        Line(sb, total - RailMargin, 0, total - RailMargin, height, "lad-rail");
        Draw(sb, rung, RailMargin, 0, highlight);
        Line(sb, RailMargin + width, WireY, total - RailMargin, WireY, "lad-wire");

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static (int Width, int Height) Measure(LadderNode node) => node switch
    {
        LadderInstruction i => MeasureInstruction(i),
        LadderSeries s when s.Items.Count == 0 => (30, CellHeight),
        LadderSeries s => (s.Items.Sum(n => Measure(n).Width), s.Items.Max(n => Measure(n).Height)),
        LadderBranch b => (b.Legs.Max(l => Measure(l).Width) + 2 * BranchPad, b.Legs.Sum(l => Measure(l).Height)),
        _ => (0, 0)
    };

    private static (int Width, int Height) MeasureInstruction(LadderInstruction instruction)
    {
        if (Contacts.Contains(instruction.Mnemonic) || Coils.Contains(instruction.Mnemonic))
            return (SymbolWidth, CellHeight);

        var (boxWidth, boxHeight) = BoxSize(instruction);
        return (boxWidth + 20, Math.Max(CellHeight, WireY - 12 + boxHeight + 8));
    }

    private static (int Width, int Height) BoxSize(LadderInstruction instruction)
    {
        var longest = instruction.Operands.Select(o => Math.Min(o.Length, 28)).Append(instruction.Mnemonic.Length).Max();
        var width = Math.Clamp(longest * 7 + 20, 90, 220);
        var height = 20 + Math.Max(1, instruction.Operands.Count) * 14 + 6;
        return (width, height);
    }

    private static void Draw(StringBuilder sb, LadderNode node, int x, int y, Func<LadderInstruction, string?>? highlight)
    {
        switch (node)
        {
            case LadderInstruction instruction:
                DrawInstruction(sb, instruction, x, y, highlight?.Invoke(instruction));
                break;

            case LadderSeries series when series.Items.Count == 0:
                Line(sb, x, y + WireY, x + 30, y + WireY, "lad-wire");
                break;

            case LadderSeries series:
                foreach (var item in series.Items)
                {
                    Draw(sb, item, x, y, highlight);
                    x += Measure(item).Width;
                }
                break;

            case LadderBranch branch:
            {
                var width = Measure(branch).Width;
                var legY = y;
                var lastWire = y + WireY;
                foreach (var leg in branch.Legs)
                {
                    var (legWidth, legHeight) = Measure(leg);
                    Line(sb, x, legY + WireY, x + BranchPad, legY + WireY, "lad-wire");
                    Draw(sb, leg, x + BranchPad, legY, highlight);
                    Line(sb, x + BranchPad + legWidth, legY + WireY, x + width, legY + WireY, "lad-wire");
                    lastWire = legY + WireY;
                    legY += legHeight;
                }
                Line(sb, x, y + WireY, x, lastWire, "lad-wire");
                Line(sb, x + width, y + WireY, x + width, lastWire, "lad-wire");
                break;
            }
        }
    }

    private static void DrawInstruction(StringBuilder sb, LadderInstruction instruction, int x, int y, string? cssClass)
    {
        var (width, _) = MeasureInstruction(instruction);
        var wire = y + WireY;
        var title = Encode(instruction.Key);
        sb.Append($"<g class=\"lad-instr {cssClass}\"><title>{title}</title>");

        if (Contacts.Contains(instruction.Mnemonic) || Coils.Contains(instruction.Mnemonic))
        {
            var cx = x + width / 2;
            Line(sb, x, wire, cx - 9, wire, "lad-wire");
            Line(sb, cx + 9, wire, x + width, wire, "lad-wire");

            var label = instruction.Operands.Count > 0 ? instruction.Operands[0] : "";
            // Keep the end of long names: for dotted tags (Unit.Station.Start) it identifies the bit.
            if (label.Length > MaxLabel)
                label = "…" + label[^(MaxLabel - 1)..];
            Text(sb, cx, wire - 16, label, "lad-text", anchor: "middle");

            var mnemonic = instruction.Mnemonic.ToUpperInvariant();
            if (Contacts.Contains(mnemonic))
            {
                Line(sb, cx - 9, wire - 10, cx - 9, wire + 10, "lad-sym");
                Line(sb, cx + 9, wire - 10, cx + 9, wire + 10, "lad-sym");
                if (mnemonic == "XIO")
                    Line(sb, cx - 7, wire + 9, cx + 7, wire - 9, "lad-sym");
                if (mnemonic is "XICP" or "XICN")
                    Text(sb, cx, wire + 4, mnemonic == "XICP" ? "P" : "N", "lad-mark", anchor: "middle");
            }
            else
            {
                sb.Append(CultureInfo.InvariantCulture,
                    $"<path class=\"lad-sym\" d=\"M{cx - 4} {wire - 10} A 12 12 0 0 0 {cx - 4} {wire + 10} M{cx + 4} {wire - 10} A 12 12 0 0 1 {cx + 4} {wire + 10}\" fill=\"none\"/>");
                var mark = mnemonic switch { "OTL" => "L", "OTU" => "U", "OTN" => "/", _ => null };
                if (mark != null)
                    Text(sb, cx, wire + 4, mark, "lad-mark", anchor: "middle");
            }
        }
        else
        {
            var (boxWidth, boxHeight) = BoxSize(instruction);
            var bx = x + 10;
            var by = wire - 12;
            Line(sb, x, wire, bx, wire, "lad-wire");
            Line(sb, bx + boxWidth, wire, x + width, wire, "lad-wire");
            sb.Append(CultureInfo.InvariantCulture,
                $"<rect class=\"lad-box\" x=\"{bx}\" y=\"{by}\" width=\"{boxWidth}\" height=\"{boxHeight}\" rx=\"3\"/>");
            Text(sb, bx + boxWidth / 2, by + 14, instruction.Mnemonic, "lad-head", anchor: "middle");
            var lineY = by + 30;
            foreach (var operand in instruction.Operands)
            {
                var shown = operand.Length > 28 ? operand[..27] + "…" : operand;
                Text(sb, bx + 6, lineY, shown, "lad-text", anchor: "start");
                lineY += 14;
            }
        }

        sb.Append("</g>");
    }

    private static void Line(StringBuilder sb, int x1, int y1, int x2, int y2, string cssClass) =>
        sb.Append(CultureInfo.InvariantCulture, $"<line class=\"{cssClass}\" x1=\"{x1}\" y1=\"{y1}\" x2=\"{x2}\" y2=\"{y2}\"/>");

    private static void Text(StringBuilder sb, int x, int y, string text, string cssClass, string anchor) =>
        sb.Append(CultureInfo.InvariantCulture,
            $"<text class=\"{cssClass}\" x=\"{x}\" y=\"{y}\" text-anchor=\"{anchor}\">{Encode(text)}</text>");

    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}
