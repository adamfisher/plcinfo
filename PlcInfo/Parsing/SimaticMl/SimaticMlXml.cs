using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace PlcInfo.Parsing.SimaticMl;

/// <summary>
/// Loading, detection and common element helpers for TIA Portal Openness (SimaticML) XML.
/// </summary>
/// <remarks>
/// Envelope (spec §1): an un-namespaced <c>Document</c> root holding <c>Engineering</c>,
/// optional <c>DocumentInfo</c>, and exactly one <c>SW.*</c> engineering object. Properties
/// are child elements of <c>AttributeList</c>; only embedded islands (Interface, FlgNet,
/// StructuredText, StatementList, Graph) are namespaced, with a version suffix that changes
/// between TIA releases, so elements are matched by local name.
/// </remarks>
internal static class SimaticMlXml
{
    /// <summary>
    /// Upper bound for a single export (characters). Real block exports are well under 1 MB.
    /// </summary>
    private const long MaxDocumentCharacters = 256L * 1024 * 1024;

    private static readonly Regex QuotedName = new("^\"(?<n>[^\"]*)\"$", RegexOptions.CultureInvariant);

    private static readonly Regex PlainIdentifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Reader settings for untrusted input: no DTDs, no external resolution, bounded size.
    /// </summary>
    internal static XmlReaderSettings ReaderSettings(bool async = false) => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = MaxDocumentCharacters,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        Async = async
    };

    /// <summary>
    /// Returns true when <paramref name="filePath"/> is a SimaticML export: an un-namespaced
    /// <c>Document</c> root whose first engineering object is an <c>SW.*</c> element.
    /// Only the head of the file is read.
    /// </summary>
    internal static bool IsSimaticMlFile(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
            using var reader = XmlReader.Create(stream, ReaderSettings());

            if (reader.MoveToContent() != XmlNodeType.Element ||
                reader.LocalName != "Document" || reader.NamespaceURI.Length != 0 || reader.IsEmptyElement)
                return false;

            reader.Read();
            while (!reader.EOF)
            {
                if (reader.NodeType == XmlNodeType.EndElement)
                    return false;
                if (reader.NodeType != XmlNodeType.Element)
                {
                    reader.Read();
                    continue;
                }

                var name = reader.LocalName;
                if (name.StartsWith("SW.", StringComparison.Ordinal))
                    return true;
                if (name is not ("Engineering" or "DocumentInfo"))
                    return false;
                reader.Skip();
            }
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
        }

        return false;
    }

    /// <summary>
    /// Loads a SimaticML document safely.
    /// </summary>
    internal static async Task<XDocument> LoadAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        using var reader = XmlReader.Create(stream, ReaderSettings(async: true));
        return await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
    }

    /// <summary>
    /// First child element with the given local name, in any namespace.
    /// </summary>
    internal static XElement? Child(this XElement? element, string localName) =>
        element?.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    /// <summary>
    /// Child elements with the given local name, in any namespace.
    /// </summary>
    internal static IEnumerable<XElement> Children(this XElement? element, string localName) =>
        element?.Elements().Where(e => e.Name.LocalName == localName) ?? Enumerable.Empty<XElement>();

    /// <summary>
    /// Value of <c>AttributeList/&lt;name&gt;</c> of an engineering object, or null.
    /// </summary>
    internal static string? Attr(this XElement obj, string name)
    {
        var value = obj.Child("AttributeList").Child(name);
        return value?.Value;
    }

    /// <summary>
    /// Text of the <c>MultilingualText</c> object with the given <c>CompositionName</c>
    /// (block/network/tag Comment or Title). Prefers en-US, then the first non-empty culture
    /// in document order; returns null when every culture is empty.
    /// </summary>
    internal static string? MultilingualText(this XElement obj, string compositionName)
    {
        var text = obj.Child("ObjectList").Children("MultilingualText")
            .FirstOrDefault(t => (string?)t.Attribute("CompositionName") == compositionName);
        if (text == null)
            return null;

        var items = text.Child("ObjectList").Children("MultilingualTextItem")
            .Select(i => (Culture: i.Attr("Culture"), Text: i.Attr("Text")))
            .Where(i => !string.IsNullOrWhiteSpace(i.Text))
            .ToList();

        var chosen = items.FirstOrDefault(i => i.Culture == "en-US").Text ?? items.FirstOrDefault().Text;
        return chosen;
    }

    /// <summary>
    /// Text of an interface-style <c>&lt;Comment&gt;&lt;MultiLanguageText Lang=".."&gt;</c> element
    /// (spec §2: member comments use this form, not MultilingualText objects).
    /// </summary>
    internal static string? MultiLanguageComment(XElement? comment)
    {
        if (comment == null)
            return null;
        var items = comment.Children("MultiLanguageText")
            .Select(t => (Lang: (string?)t.Attribute("Lang"), Text: t.Value))
            .Where(t => !string.IsNullOrWhiteSpace(t.Text))
            .ToList();
        return items.FirstOrDefault(t => t.Lang == "en-US").Text ?? items.FirstOrDefault().Text;
    }

    /// <summary>
    /// Removes the double quotes TIA puts around user type names (<c>"udt_Fifo"</c> → <c>udt_Fifo</c>).
    /// </summary>
    internal static string Unquote(string value)
    {
        var match = QuotedName.Match(value.Trim());
        return match.Success ? match.Groups["n"].Value : value.Trim();
    }

    /// <summary>
    /// Quotes a symbol name the way TIA displays it when it is not a plain identifier.
    /// </summary>
    internal static string QuoteIfNeeded(string name) =>
        PlainIdentifier.IsMatch(name) ? name : $"\"{name}\"";

    /// <summary>
    /// Integer attribute value, or null.
    /// </summary>
    internal static int? IntAttribute(XElement element, string name) =>
        int.TryParse((string?)element.Attribute(name), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
}
