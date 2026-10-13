using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace PlcInfo.Parsing.SimaticMl;

/// <summary>
/// Renders SimaticML <c>Access</c> elements (operands) as TIA displays them.
/// </summary>
/// <remarks>
/// Spec §3: symbols are a sequence of <c>Component</c>s; an index is a nested <c>Access</c>
/// inside the Component. The <c>#</c> prefix for locals and the quotes around global names are
/// not stored, so they are added back here: <c>#Timers.CircuitProtection</c>,
/// <c>"DB_DI".in_SelStart</c>.
/// </remarks>
internal static class SimaticMlOperand
{
    /// <summary>
    /// Renders an <c>Access</c> (or <c>Instance</c>) element. <paramref name="tokenRenderer"/>
    /// renders SCL token content found inside symbols (SCL exports interleave
    /// <c>Token Text="."</c> and bracket tokens with the components).
    /// </summary>
    internal static string Render(XElement access, Func<XElement, string>? tokenRenderer = null)
    {
        var scope = (string?)access.Attribute("Scope") ?? string.Empty;
        switch (scope)
        {
            case "LiteralConstant":
            case "TypedConstant":
                return RenderConstant(access.Child("Constant")) ?? access.Value.Trim();
            case "Address":
                return RenderAddress(access.Child("Address")) ?? "%?";
            case "Label":
                return (string?)access.Child("Label")?.Attribute("Name") ?? access.Value.Trim();
        }

        // An Instance element holds its Components directly; an Access wraps them in a Symbol.
        var symbol = access.Child("Symbol") ?? (access.Name.LocalName == "Instance" ? access : null);
        if (symbol == null)
        {
            var constant = access.Child("Constant");
            if (constant != null)
                return RenderConstant(constant) ?? string.Empty;
            return $"<{scope}>";
        }

        var global = scope is "GlobalVariable" or "GlobalConstant";
        var text = RenderSymbol(symbol, global, tokenRenderer);
        return scope is "LocalVariable" or "LocalConstant" ? "#" + text : text;
    }

    /// <summary>
    /// Renders a <c>Symbol</c>: components joined by dots, the first one quoted for globals.
    /// </summary>
    private static string RenderSymbol(XElement symbol, bool global, Func<XElement, string>? tokenRenderer)
    {
        var sb = new StringBuilder();
        var first = true;
        var hasTokens = symbol.Elements().Any(e => e.Name.LocalName == "Token");

        foreach (var child in symbol.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "Component":
                    if (!hasTokens && !first)
                        sb.Append('.');
                    sb.Append(RenderComponent(child, first && global, tokenRenderer));
                    first = false;
                    break;
                case "Token":
                    sb.Append((string?)child.Attribute("Text"));
                    break;
                case "Address":
                    // Informative absolute address under a symbol (ExportOptions.WithReadOnly).
                    break;
                default:
                    if (tokenRenderer != null)
                        sb.Append(tokenRenderer(child));
                    break;
            }
        }

        return sb.ToString();
    }

    private static string RenderComponent(XElement component, bool quote, Func<XElement, string>? tokenRenderer)
    {
        var name = (string?)component.Attribute("Name") ?? string.Empty;
        var sb = new StringBuilder(quote ? $"\"{name}\"" : SimaticMlXml.QuoteIfNeeded(name));

        var children = component.Elements().ToList();
        if (children.Count > 0)
        {
            if (children.Any(c => c.Name.LocalName == "Token") && tokenRenderer != null)
            {
                // SCL: brackets and separators are explicit tokens.
                foreach (var child in children)
                    sb.Append(tokenRenderer(child));
            }
            else
            {
                var indexes = children.Where(c => c.Name.LocalName == "Access")
                    .Select(c => Render(c, tokenRenderer)).ToList();
                if (indexes.Count > 0)
                    sb.Append('[').Append(string.Join(", ", indexes)).Append(']');
            }
        }

        // Bit/byte/word slice access, e.g. SliceAccessModifier="x0" → .%X0.
        var slice = (string?)component.Attribute("SliceAccessModifier");
        if (!string.IsNullOrEmpty(slice))
            sb.Append(".%").Append(slice.ToUpperInvariant());

        return sb.ToString();
    }

    /// <summary>
    /// Renders a <c>Constant</c> element as its literal value.
    /// </summary>
    internal static string? RenderConstant(XElement? constant)
    {
        if (constant == null)
            return null;
        var value = constant.Child("ConstantValue")?.Value;
        return value ?? constant.Value.Trim();
    }

    /// <summary>
    /// Renders an absolute <c>Address</c> (spec §3, V14 SP1 table): Area, Type, BitOffset,
    /// and BlockNumber for DB accesses, e.g. Area=Input Type=Bool BitOffset=1 → <c>%I0.1</c>.
    /// </summary>
    internal static string? RenderAddress(XElement? address)
    {
        if (address == null)
            return null;

        var area = (string?)address.Attribute("Area") ?? string.Empty;
        var type = (string?)address.Attribute("Type") ?? string.Empty;
        var bitOffset = SimaticMlXml.IntAttribute(address, "BitOffset") ?? 0;
        var blockNumber = SimaticMlXml.IntAttribute(address, "BlockNumber");

        var prefix = area switch
        {
            "Input" => "I",
            "Output" => "Q",
            "Memory" => "M",
            "Local" => "L",
            "PeripheryInput" => "I",
            "PeripheryOutput" => "Q",
            "DB" or "DI" => "DB",
            "Timer" => "T",
            "Counter" => "C",
            _ => area
        };

        if (prefix is "T" or "C")
            return string.Create(CultureInfo.InvariantCulture, $"%{prefix}{bitOffset}");

        var size = SizeLetter(type);
        var byteOffset = bitOffset / 8;
        var bit = bitOffset % 8;
        var periphery = area.StartsWith("Periphery", StringComparison.Ordinal) ? ":P" : string.Empty;

        if (prefix == "DB")
        {
            var db = blockNumber.HasValue ? string.Create(CultureInfo.InvariantCulture, $"%DB{blockNumber}.") : "%";
            return size.Length == 0
                ? string.Create(CultureInfo.InvariantCulture, $"{db}DBX{byteOffset}.{bit}")
                : string.Create(CultureInfo.InvariantCulture, $"{db}DB{size}{byteOffset}");
        }

        return size.Length == 0
            ? string.Create(CultureInfo.InvariantCulture, $"%{prefix}{byteOffset}.{bit}{periphery}")
            : string.Create(CultureInfo.InvariantCulture, $"%{prefix}{size}{byteOffset}{periphery}");
    }

    private static string SizeLetter(string type) => type.ToUpperInvariant() switch
    {
        "BOOL" => string.Empty,
        "BYTE" or "CHAR" or "SINT" or "USINT" => "B",
        "WORD" or "INT" or "UINT" or "S5TIME" or "DATE" or "WCHAR" => "W",
        _ => "D"
    };
}
