using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using DriverCompass.Core.Models;

namespace DriverCompass.VersionProviders.Xml;

/// <summary>
/// Hardened XML reading for downloaded catalogs: no DTDs (no entity expansion / XXE), no external resolution,
/// size cap. Large catalogs are read element by element to keep memory low.
/// </summary>
internal static class SafeXml
{
    private const long MaxCharacters = 250_000_000;

    public static XmlReader CreateReader(Stream stream) => XmlReader.Create(stream, new XmlReaderSettings
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = MaxCharacters,
        MaxCharactersFromEntities = 0,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
        CloseInput = false,
    });

    /// <summary>Streams the root's direct children whose local name is accepted; <paramref name="onRoot"/> sees the root element (attributes).</summary>
    public static IEnumerable<XElement> ReadChildren(Stream stream, Func<string, bool> wanted, Action<XmlReader>? onRoot = null)
    {
        using var reader = CreateReader(stream);
        try
        {
            if (reader.MoveToContent() != XmlNodeType.Element)
            {
                throw Invalid("no root element");
            }
        }
        catch (XmlException ex)
        {
            throw Invalid(ex.Message, ex);
        }

        onRoot?.Invoke(reader);
        if (reader.IsEmptyElement)
        {
            yield break;
        }

        Read(reader);
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == 0)
            {
                yield break;
            }

            if (reader.NodeType == XmlNodeType.Element)
            {
                if (wanted(reader.LocalName))
                {
                    XElement element;
                    try
                    {
                        element = (XElement)XNode.ReadFrom(reader);
                    }
                    catch (XmlException ex)
                    {
                        throw Invalid(ex.Message, ex);
                    }

                    yield return element;
                }
                else
                {
                    Skip(reader);
                }

                continue;
            }

            Read(reader);
        }
    }

    /// <summary>Loads a small document completely.</summary>
    public static XElement Load(Stream stream)
    {
        using var reader = CreateReader(stream);
        try
        {
            return XElement.Load(reader);
        }
        catch (XmlException ex)
        {
            throw Invalid(ex.Message, ex);
        }
    }

    // ---- namespace-agnostic helpers (Dell uses a default namespace, others none) ----

    public static IEnumerable<XElement> Kids(this XElement e, string localName) =>
        e.Elements().Where(x => x.Name.LocalName == localName);

    public static XElement? Kid(this XElement e, string localName) => e.Kids(localName).FirstOrDefault();

    public static IEnumerable<XElement> Below(this XElement e, string localName) =>
        e.Descendants().Where(x => x.Name.LocalName == localName);

    public static string? Attr(this XElement? e, string name) =>
        e?.Attribute(name)?.Value is { } v && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    public static string? Text(this XElement? e) =>
        e?.Value is { } v && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    public static DateOnly? ParseDate(string? text, params string[] formats) =>
        text is not null && DateOnly.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d)
            ? d
            : null;

    public static VersionSourceException Invalid(string message, Exception? inner = null) =>
        new(VersionSourceReasons.InvalidData, "Invalid catalog XML: " + message, inner);

    private static void Read(XmlReader reader)
    {
        try
        {
            reader.Read();
        }
        catch (XmlException ex)
        {
            throw Invalid(ex.Message, ex);
        }
    }

    private static void Skip(XmlReader reader)
    {
        try
        {
            reader.Skip();
        }
        catch (XmlException ex)
        {
            throw Invalid(ex.Message, ex);
        }
    }
}
