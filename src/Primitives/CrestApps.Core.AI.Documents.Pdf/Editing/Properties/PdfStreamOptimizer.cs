using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Objects;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// Makes a PDF smaller without changing what it shows: removes page thumbnails and private metadata, merges
/// identical objects, and compresses streams losslessly.
/// </summary>
/// <remarks>
/// Nothing here decodes or re-encodes an image — no image codec is available — so the gains come from
/// structure: files assembled from pieces often carry the same logo, font or form many times over, and
/// streams written uncompressed or with a fast compression level. Unreachable objects are dropped by the
/// save itself.
/// </remarks>
internal static class PdfStreamOptimizer
{
    private static readonly HashSet<string> _keptInformation = new(StringComparer.Ordinal)
    {
        "/Producer",
        "/CreationDate",
        "/ModDate",
    };

    /// <summary>
    /// Removes the page thumbnails a file carries; viewers draw their own.
    /// </summary>
    /// <param name="document">The document, opened for editing.</param>
    /// <returns>The number of thumbnails removed.</returns>
    public static int RemoveThumbnails(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var removed = 0;

        foreach (var page in document.Pages)
        {
            if (page.Elements.Remove("/Thumb"))
            {
                removed++;
            }
        }

        return removed;
    }

    /// <summary>
    /// Removes the document information (title, author and the rest, keeping the producer and the dates),
    /// the metadata packets of pages and objects, and applications' private data.
    /// </summary>
    /// <param name="document">The document, opened for editing.</param>
    /// <returns>The number of entries removed.</returns>
    public static int RemoveMetadata(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var removed = 0;
        var info = document.Info.Dictionary;

        foreach (var key in info?.Elements.Keys.ToList() ?? [])
        {
            if (!_keptInformation.Contains(key) && info.Elements.Remove(key))
            {
                removed++;
            }
        }

        var catalog = document.Catalog;

        if (catalog.Elements.Remove("/PieceInfo"))
        {
            removed++;
        }

        foreach (var item in document.GetAllObjects())
        {
            if (item is PdfDictionary dictionary && !ReferenceEquals(dictionary, catalog))
            {
                removed += (dictionary.Elements.Remove("/Metadata") ? 1 : 0) + (dictionary.Elements.Remove("/PieceInfo") ? 1 : 0);
            }
        }

        return removed;
    }

    /// <summary>
    /// Merges streams that are byte-for-byte the same — images, fonts, forms — into one object that every
    /// user refers to.
    /// </summary>
    /// <param name="document">The document, opened for editing.</param>
    /// <returns>The number of duplicate objects merged away.</returns>
    public static int Deduplicate(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var pageContents = new HashSet<PdfObject>(ReferenceEqualityComparer.Instance);

        foreach (var page in document.Pages)
        {
            foreach (var item in PdfObjects.Items(page.Elements["/Contents"]))
            {
                if (PdfObjects.AsObject(item) is { } content)
                {
                    pageContents.Add(content);
                }
            }
        }

        var merged = 0;

        // An image's soft mask is a stream of its own, so two copies of an image only look the same once their
        // masks were merged; a few passes catch such chains.
        for (var pass = 0; pass < 3; pass++)
        {
            var canonical = new Dictionary<string, PdfDictionary>(StringComparer.Ordinal);
            var replacements = new Dictionary<PdfObject, PdfReference>(ReferenceEqualityComparer.Instance);
            var objects = document.GetAllObjects();

            // Objects nothing refers to any more (a removed thumbnail) are dropped by the save; they are not
            // duplicates worth counting.
            var reachable = Reachable(document);

            foreach (var item in objects)
            {
                if (item is not PdfDictionary dictionary ||
                    !reachable.Contains(dictionary) ||
                    dictionary.Stream is null ||
                    dictionary.Reference is null ||
                    pageContents.Contains(dictionary))
                {
                    continue;
                }

                var key = KeyOf(dictionary);

                if (canonical.TryGetValue(key, out var first))
                {
                    replacements[dictionary] = first.Reference;
                }
                else
                {
                    canonical[key] = dictionary;
                }
            }

            if (replacements.Count == 0)
            {
                break;
            }

            foreach (var item in objects)
            {
                if (!replacements.ContainsKey(item))
                {
                    Rewrite(item, replacements, 0);
                }
            }

            foreach (var page in document.Pages)
            {
                Rewrite(page, replacements, 0);
            }

            merged += replacements.Count;
        }

        return merged;
    }

    /// <summary>
    /// Compresses the streams a file stores without any filter.
    /// </summary>
    /// <param name="document">The document, opened for editing.</param>
    /// <returns>The number of streams compressed.</returns>
    public static int CompressUncompressed(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var compressed = 0;
        var reachable = Reachable(document);

        foreach (var item in document.GetAllObjects())
        {
            if (item is not PdfDictionary dictionary ||
                !reachable.Contains(dictionary) ||
                dictionary.Stream is null ||
                dictionary.Elements["/Filter"] is not null ||
                dictionary.Elements["/F"] is not null ||
                string.Equals(PdfObjects.GetName(dictionary, "/Type"), "Metadata", StringComparison.Ordinal))
            {
                continue;
            }

            var raw = dictionary.Stream.Value;

            if (raw.Length < 64)
            {
                continue;
            }

            var encoded = PdfStream.Compress(raw, CompressionLevel.SmallestSize);

            if (encoded.Length < raw.Length * 0.95)
            {
                dictionary.Stream.Value = encoded;
                dictionary.Elements.SetName("/Filter", "/FlateDecode");
                dictionary.Elements.Remove("/DecodeParms");
                compressed++;
            }
        }

        return compressed;
    }

    /// <summary>
    /// Compresses again, at the best level, the streams that are compressed with Flate and no predictor.
    /// </summary>
    /// <param name="document">The document, opened for editing.</param>
    /// <returns>The number of streams that became smaller.</returns>
    public static int Recompress(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var recompressed = 0;
        var reachable = Reachable(document);

        foreach (var item in document.GetAllObjects())
        {
            if (item is not PdfDictionary dictionary ||
                !reachable.Contains(dictionary) ||
                dictionary.Stream is null ||
                dictionary.Elements["/DecodeParms"] is not null ||
                !IsFlateOnly(dictionary.Elements["/Filter"]))
            {
                continue;
            }

            var raw = dictionary.Stream.Value;

            if (raw.Length < 256)
            {
                continue;
            }

            var decoded = PdfStreamFilters.Inflate(raw);

            if (decoded is null || decoded.Length == 0)
            {
                continue;
            }

            var encoded = PdfStream.Compress(decoded, CompressionLevel.SmallestSize);

            if (encoded.Length < raw.Length * 0.98)
            {
                dictionary.Stream.Value = encoded;
                dictionary.Elements.SetName("/Filter", "/FlateDecode");
                recompressed++;
            }
        }

        return recompressed;
    }

    private static HashSet<PdfObject> Reachable(PdfDocument document)
    {
        var reachable = new HashSet<PdfObject>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<PdfItem>();

        pending.Push(document.Catalog);

        if (document.Info.Dictionary is { } info)
        {
            pending.Push(info);
        }

        while (pending.Count > 0)
        {
            var item = pending.Pop();

            if (item is PdfReference reference)
            {
                item = reference.Value;
            }

            switch (item)
            {
                case PdfDictionary dictionary:
                    if (!reachable.Add(dictionary))
                    {
                        continue;
                    }

                    foreach (var entry in dictionary.Elements)
                    {
                        if (entry.Value is not null)
                        {
                            pending.Push(entry.Value);
                        }
                    }

                    break;
                case PdfArray array:
                    if (!reachable.Add(array))
                    {
                        continue;
                    }

                    foreach (var element in array.Elements)
                    {
                        pending.Push(element);
                    }

                    break;
            }
        }

        return reachable;
    }

    private static bool IsFlateOnly(PdfItem filter)
    {
        return PdfObjects.Resolve(filter) switch
        {
            PdfName name => name.Value is "/FlateDecode" or "/Fl",
            PdfArray array => array.Elements.Count == 1 && PdfObjects.Resolve(array.Elements[0]) is PdfName { Value: "/FlateDecode" or "/Fl" },
            _ => false,
        };
    }

    private static string KeyOf(PdfDictionary dictionary)
    {
        var description = new StringBuilder();

        Describe(dictionary, description, 0, skipLength: true);

        var header = SHA256.HashData(Encoding.UTF8.GetBytes(description.ToString()));
        var body = SHA256.HashData(dictionary.Stream.Value);

        return Convert.ToHexString(header) + Convert.ToHexString(body);
    }

    private static void Describe(PdfItem item, StringBuilder builder, int depth, bool skipLength = false)
    {
        if (depth > 16)
        {
            builder.Append('…');

            return;
        }

        switch (item)
        {
            case PdfReference reference:
                builder.Append('R').Append(reference.ObjectNumber.ToString(CultureInfo.InvariantCulture)).Append('.').Append(reference.Generation.ToString(CultureInfo.InvariantCulture));

                break;
            case PdfDictionary dictionary:
                builder.Append("<<");

                // Entries are compared by key, whatever order the file wrote them in.
                foreach (var key in dictionary.Elements.Keys.Order(StringComparer.Ordinal))
                {
                    if (skipLength && key == "/Length")
                    {
                        continue;
                    }

                    builder.Append(key).Append(' ');
                    Describe(dictionary.Elements[key], builder, depth + 1);
                    builder.Append(' ');
                }

                builder.Append(">>");

                break;
            case PdfArray array:
                builder.Append('[');

                foreach (var element in array.Elements)
                {
                    Describe(element, builder, depth + 1);
                    builder.Append(' ');
                }

                builder.Append(']');

                break;
            case PdfString text:
                builder.Append("S<").Append(Convert.ToHexString(text.Bytes)).Append('>');

                break;
            case PdfName name:
                builder.Append(name.Value);

                break;
            case PdfInteger number:
                builder.Append('I').Append(number.Value.ToString(CultureInfo.InvariantCulture));

                break;
            case PdfReal number:
                builder.Append('D').Append(number.Value.ToString("R", CultureInfo.InvariantCulture));

                break;
            case PdfBoolean flag:
                builder.Append(flag.Value ? "true" : "false");

                break;
            case PdfRectangle rectangle:
                builder.Append(CultureInfo.InvariantCulture, $"[{rectangle.X1:R} {rectangle.Y1:R} {rectangle.X2:R} {rectangle.Y2:R}]");

                break;
            case null:
            case PdfNull:
                builder.Append("null");

                break;
            default:
                // Anything else is never taken for something else: it is unique to this object.
                builder.Append(item.GetType().Name).Append(':').Append(RuntimeHelpers.GetHashCode(item).ToString(CultureInfo.InvariantCulture));

                break;
        }
    }

    private static void Rewrite(PdfItem item, Dictionary<PdfObject, PdfReference> replacements, int depth)
    {
        if (depth > 32)
        {
            return;
        }

        switch (item)
        {
            case PdfDictionary dictionary:
                foreach (var key in dictionary.Elements.Keys.ToList())
                {
                    var value = dictionary.Elements[key];

                    if (value is PdfReference reference && reference.Value is PdfObject target && replacements.TryGetValue(target, out var replacement))
                    {
                        dictionary.Elements[key] = replacement;
                    }
                    else if (value is PdfObject { Reference: null } direct)
                    {
                        Rewrite(direct, replacements, depth + 1);
                    }
                }

                break;
            case PdfArray array:
                for (var index = 0; index < array.Elements.Count; index++)
                {
                    var value = array.Elements[index];

                    if (value is PdfReference reference && reference.Value is PdfObject target && replacements.TryGetValue(target, out var replacement))
                    {
                        array.Elements[index] = replacement;
                    }
                    else if (value is PdfObject { Reference: null } direct)
                    {
                        Rewrite(direct, replacements, depth + 1);
                    }
                }

                break;
        }
    }
}
