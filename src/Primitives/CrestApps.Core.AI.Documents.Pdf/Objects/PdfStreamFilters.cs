using UglyToad.PdfPig;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// Undoes the encoding of stream data with PdfPig's filters.
/// </summary>
internal static class PdfStreamFilters
{
    private static readonly HashSet<string> _lossless = new(StringComparer.Ordinal)
    {
        "/FlateDecode",
        "/Fl",
        "/LZWDecode",
        "/LZW",
        "/ASCIIHexDecode",
        "/AHx",
        "/ASCII85Decode",
        "/A85",
        "/RunLengthDecode",
        "/RL",
    };

    /// <summary>
    /// Lists the filters a stream's dictionary names, in the order they were applied.
    /// </summary>
    /// <param name="dictionary">The stream's dictionary.</param>
    /// <returns>The filter names with their slash.</returns>
    public static List<string> Names(PdfDictionary dictionary)
    {
        return dictionary?.Elements.GetResolved("/Filter") switch
        {
            PdfName name => [name.Value],
            PdfArray array => [.. array.Elements.Select(PdfObjectModel.Resolve).OfType<PdfName>().Select(name => name.Value)],
            _ => [],
        };
    }

    /// <summary>
    /// Decodes stream data through its lossless filters.
    /// </summary>
    /// <param name="dictionary">The stream's dictionary.</param>
    /// <param name="value">The bytes as stored.</param>
    /// <returns>The decoded bytes, or <paramref name="value"/> when it uses another filter or does not decode.</returns>
    public static byte[] Decode(PdfDictionary dictionary, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(value);

        var filters = Names(dictionary);

        if (filters.Count == 0 || !filters.All(_lossless.Contains))
        {
            return value;
        }

        var entries = new Dictionary<NameToken, IToken>
        {
            [NameToken.Filter] = PdfObjectModel.ToToken(dictionary.Elements.GetResolved("/Filter")),
        };

        if (dictionary.Elements.GetResolved("/DecodeParms") is { } parameters)
        {
            entries[NameToken.DecodeParms] = PdfObjectModel.ToToken(parameters);
        }

        try
        {
            return new StreamToken(new DictionaryToken(entries), value).Decode(DefaultFilterProvider.Instance).ToArray();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A damaged stream is kept as it is stored, as a reader would show it: as nothing it can decode.
            return value;
        }
    }
}
