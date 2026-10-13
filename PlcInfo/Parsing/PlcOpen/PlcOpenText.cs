using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace PlcInfo.Parsing.PlcOpen;

/// <summary>
/// Text helpers for PLCopen TC6 XML: data type and initial value rendering, formatted
/// (XHTML) text, and IEC 61131-3 / ISO 8601 durations. All helpers expect element names
/// without namespaces (the parser strips them).
/// </summary>
internal static class PlcOpenText
{
    // Nested types/values deeper than this are cut short; real exports nest a few levels.
    private const int MaxDepth = 32;

    private static readonly HashSet<string> TypeWrappers = new(StringComparer.Ordinal)
    {
        "type", "baseType", "returnType"
    };

    /// <summary>
    /// Renders a PLCopen data type (TC6 §"ppx:dataType") in IEC 61131-3 declaration syntax.
    /// Accepts either a wrapper (<c>type</c>, <c>baseType</c>, <c>returnType</c>) or the type element itself.
    /// </summary>
    /// <returns>The type text, e.g. <c>ARRAY[0..MAX] OF INT</c>; empty when no type is present.</returns>
    public static string FormatType(XElement? element) => FormatType(element, 0);

    private static string FormatType(XElement? element, int depth)
    {
        if (element == null)
            return string.Empty;
        if (depth > MaxDepth)
            return "...";

        if (TypeWrappers.Contains(element.Name.LocalName))
        {
            element = element.Elements().FirstOrDefault(e => e.Name.LocalName is not ("addData" or "documentation"));
            if (element == null)
                return string.Empty;
        }

        var name = element.Name.LocalName;
        switch (name)
        {
            case "derived":
                return element.Attribute("name")?.Value ?? string.Empty;
            case "string":
            case "wstring":
                var length = element.Attribute("length")?.Value;
                var upper = name.ToUpperInvariant();
                return string.IsNullOrEmpty(length) ? upper : $"{upper}({length})";
            case "array":
                var bounds = ArrayBounds(element);
                var arrayBase = FormatType(element.Element("baseType"), depth + 1);
                return $"ARRAY[{string.Join(", ", bounds)}] OF {arrayBase}";
            case "pointer":
                return $"POINTER TO {FormatType(element.Element("baseType"), depth + 1)}";
            case "reference":
                return $"REFERENCE TO {FormatType(element.Element("baseType"), depth + 1)}";
            case "subrangeSigned":
            case "subrangeUnsigned":
                var range = element.Element("range");
                return $"{FormatType(element.Element("baseType"), depth + 1)}({range?.Attribute("lower")?.Value}..{range?.Attribute("upper")?.Value})";
            case "enum":
                var values = element.Element("values")?.Elements("value")
                    .Select(v => v.Attribute("value") is { } value
                        ? $"{v.Attribute("name")?.Value} := {value.Value}"
                        : v.Attribute("name")?.Value ?? string.Empty) ?? Enumerable.Empty<string>();
                var enumBase = FormatType(element.Element("baseType"), depth + 1);
                return $"{enumBase}({string.Join(", ", values)})";
            case "struct":
                return "STRUCT";
            case "union":
                return "UNION";
            default:
                // Elementary and generic types are empty elements named after the type (<TIME/>, <LREAL/>, <ANY_NUM/>).
                return name;
        }
    }

    /// <summary>
    /// Array bounds as written (<c>lower..upper</c>); bounds are strings in the XSD and may be constant names.
    /// </summary>
    public static IReadOnlyList<string> ArrayBounds(XElement array) =>
        array.Elements("dimension")
            .Select(d => $"{d.Attribute("lower")?.Value}..{d.Attribute("upper")?.Value}")
            .ToList();

    /// <summary>
    /// The array element of a type wrapper, if the type is an array.
    /// </summary>
    public static XElement? ArrayOf(XElement? typeWrapper) =>
        typeWrapper?.Elements().FirstOrDefault(e => e.Name.LocalName == "array");

    /// <summary>
    /// Element counts per dimension, or null when the type is not an array or a bound is not a number.
    /// </summary>
    public static int[]? ArrayElementCounts(XElement? typeWrapper)
    {
        var array = ArrayOf(typeWrapper);
        if (array == null)
            return null;

        var counts = new List<int>();
        foreach (var dimension in array.Elements("dimension"))
        {
            if (!long.TryParse(dimension.Attribute("lower")?.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var lower) ||
                !long.TryParse(dimension.Attribute("upper")?.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var upper) ||
                upper < lower || upper - lower + 1 > int.MaxValue)
            {
                return null;
            }
            counts.Add((int)(upper - lower + 1));
        }
        return counts.Count > 0 ? counts.ToArray() : null;
    }

    /// <summary>
    /// Renders an <c>initialValue</c> (simpleValue, arrayValue, structValue) in IEC syntax,
    /// e.g. <c>[1, 3(0)]</c> or <c>(a := 1, b := TRUE)</c>.
    /// </summary>
    public static string? FormatValue(XElement? initialValue) =>
        initialValue == null ? null : FormatValue(initialValue, 0);

    private static string? FormatValue(XElement value, int depth)
    {
        if (depth > MaxDepth)
            return "...";

        // initialValue and arrayValue/value and structValue/value all wrap one value element.
        var inner = value.Elements().FirstOrDefault(e => e.Name.LocalName is "simpleValue" or "arrayValue" or "structValue");
        if (inner == null)
            return value.Name.LocalName == "simpleValue" ? value.Attribute("value")?.Value : null;

        switch (inner.Name.LocalName)
        {
            case "simpleValue":
                return inner.Attribute("value")?.Value;
            case "arrayValue":
                var items = inner.Elements("value").Select(v =>
                {
                    var text = FormatValue(v, depth + 1) ?? string.Empty;
                    var repeat = v.Attribute("repetitionValue")?.Value;
                    return string.IsNullOrEmpty(repeat) ? text : $"{repeat}({text})";
                });
                return $"[{string.Join(", ", items)}]";
            default:
                var members = inner.Elements("value")
                    .Select(v => $"{v.Attribute("member")?.Value} := {FormatValue(v, depth + 1)}");
                return $"({string.Join(", ", members)})";
        }
    }

    /// <summary>
    /// All text of a formattedText element (ST, IL, documentation, comment content).
    /// TC6 allows any XHTML here: CODESYS wraps the code in one <c>&lt;xhtml&gt;</c> element,
    /// the spec examples use <c>&lt;xhtml:p&gt;</c>. Paragraphs and line breaks become newlines.
    /// </summary>
    /// <returns>The text with CRLF normalised to LF, or null when the element is missing.</returns>
    public static string? FormattedText(XElement? element)
    {
        if (element == null)
            return null;

        var sb = new StringBuilder();
        foreach (var node in element.DescendantNodes())
        {
            // Indentation between the container and its xhtml child is layout, not content.
            if (node is XText { Parent: var parent } layout && parent == element && string.IsNullOrWhiteSpace(layout.Value) && element.HasElements)
                continue;

            switch (node)
            {
                case XText text:
                    sb.Append(text.Value);
                    break;
                case XElement { Name.LocalName: "br" }:
                    sb.Append('\n');
                    break;
                case XElement { Name.LocalName: "p" or "div" } when sb.Length > 0 && sb[^1] != '\n':
                    sb.Append('\n');
                    break;
            }
        }
        return sb.ToString().Replace("\r\n", "\n").Replace('\r', '\n');
    }

    /// <summary>
    /// Formatted text trimmed, or null when it is empty.
    /// </summary>
    public static string? Documentation(XElement? element)
    {
        var text = FormattedText(element)?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static readonly Regex IecDuration = new(
        @"^(?:(?:L?TIME|L?T)#)?(?<sign>[-+])?(?<parts>(?:\d+(?:\.\d+)?(?:ms|us|ns|d|h|m|s)_?)+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Regex IecDurationPart = new(
        @"(?<value>\d+(?:\.\d+)?)(?<unit>ms|us|ns|d|h|m|s)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Parses a task interval or other duration to milliseconds. Accepts IEC 61131-3 duration
    /// literals (<c>T#10ms</c>, <c>TIME#1s500ms</c>, <c>LTIME#100us</c>, unit-suffixed <c>20ms</c>)
    /// and ISO 8601 durations (<c>PT0.01S</c>, as CODESYS writes <c>task@interval</c>).
    /// </summary>
    /// <returns>Milliseconds, or null when the text is not a duration (e.g. a variable name).</returns>
    public static double? ParseDurationMs(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        text = text.Trim();

        if (text.StartsWith('P') || text.StartsWith('p') || text.StartsWith("-P", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return XmlConvert.ToTimeSpan(text.ToUpperInvariant()).TotalMilliseconds;
            }
            catch (FormatException)
            {
                return null;
            }
            catch (OverflowException)
            {
                return null;
            }
        }

        var match = IecDuration.Match(text);
        if (!match.Success)
            return null;

        double total = 0;
        foreach (Match part in IecDurationPart.Matches(match.Groups["parts"].Value))
        {
            var value = double.Parse(part.Groups["value"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            total += part.Groups["unit"].Value.ToLowerInvariant() switch
            {
                "d" => value * 86_400_000,
                "h" => value * 3_600_000,
                "m" => value * 60_000,
                "s" => value * 1_000,
                "ms" => value,
                "us" => value / 1_000,
                _ => value / 1_000_000 // ns
            };
        }
        return match.Groups["sign"].Value == "-" ? -total : total;
    }

    /// <summary>
    /// Converts a number with a unit name (CODESYS TaskSettings: Interval="20" IntervalUnit="ms") to milliseconds.
    /// </summary>
    public static double? ToMilliseconds(string? value, string? unit)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return ParseDurationMs(value);

        return (unit ?? "ms").Trim().ToLowerInvariant() switch
        {
            "s" => number * 1_000,
            "us" => number / 1_000,
            "ns" => number / 1_000_000,
            _ => number
        };
    }
}
