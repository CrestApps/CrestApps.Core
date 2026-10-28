using System.IO.Compression;
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

        // A damaged stream is kept as it is stored, as a reader would show it: as nothing it can decode.
        return TryDecode(dictionary, value, out var decoded)
            ? decoded
            : value;
    }

    /// <summary>
    /// Decodes stream data through its filters, telling whether it could.
    /// </summary>
    /// <param name="dictionary">The stream's dictionary.</param>
    /// <param name="value">The bytes as stored.</param>
    /// <param name="decoded">The decoded bytes; the stored ones when the stream names no filter.</param>
    /// <returns><see langword="false"/> when a filter is not a lossless one this can undo, or the data does not decode.</returns>
    public static bool TryDecode(PdfDictionary dictionary, byte[] value, out byte[] decoded)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(value);

        decoded = value;

        var filters = Names(dictionary);

        if (filters.Count == 0)
        {
            return true;
        }

        if (!filters.All(_lossless.Contains))
        {
            return false;
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
            decoded = new StreamToken(new DictionaryToken(entries), value).Decode(DefaultFilterProvider.Instance).ToArray();

            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            decoded = value;

            return false;
        }
    }

    /// <summary>
    /// Inflates data compressed with Flate and no predictor.
    /// </summary>
    /// <param name="value">The compressed bytes.</param>
    /// <returns>The data, or <see langword="null"/> when the bytes are not Flate data.</returns>
    public static byte[] Inflate(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);

        try
        {
            using var input = new MemoryStream(value, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();

            zlib.CopyTo(output);

            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
