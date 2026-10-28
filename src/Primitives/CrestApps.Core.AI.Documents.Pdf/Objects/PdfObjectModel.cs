using System.Globalization;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Tokens;

namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// Converts between PdfPig's tokens and the package's object model, and follows references.
/// </summary>
internal static class PdfObjectModel
{
    private const int MaxDepth = 64;

    /// <summary>
    /// Follows references to the object they name.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The direct object, or <see langword="null"/> when there is none.</returns>
    public static PdfItem Resolve(PdfItem item)
    {
        for (var depth = 0; depth < 16 && item is PdfReference reference; depth++)
        {
            item = reference.Value;
        }

        return item is PdfReference or PdfNull
            ? null
            : item;
    }

    /// <summary>
    /// Converts a PdfPig token into the object model.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <param name="owner">The document the objects belong to.</param>
    /// <param name="getReference">Returns the document's reference for an object number and generation.</param>
    /// <returns>The item.</returns>
    public static PdfItem FromToken(IToken token, PdfDocument owner, Func<IndirectReference, PdfReference> getReference)
    {
        return FromToken(token, owner, getReference, 0);
    }

    /// <summary>
    /// Converts a direct item into a PdfPig token, following references. Streams become their dictionary.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The token.</returns>
    public static IToken ToToken(PdfItem item)
    {
        return ToToken(item, 0);
    }

    private static PdfItem FromToken(IToken token, PdfDocument owner, Func<IndirectReference, PdfReference> getReference, int depth)
    {
        if (depth > MaxDepth)
        {
            return PdfNull.Value;
        }

        switch (token)
        {
            case null:
            case NullToken:
                return PdfNull.Value;

            case BooleanToken boolean:
                return boolean.Data
                    ? PdfBoolean.True
                    : PdfBoolean.False;

            case NumericToken number:
                return number.HasDecimalPlaces
                    ? new PdfReal(number.Double)
                    : new PdfInteger(number.Long);

            case NameToken name:
                return new PdfName("/" + name.Data);

            case StringToken text:
                return new PdfString(text.GetBytes(), isHex: false);

            case HexToken hex:
                return new PdfString([.. hex.Bytes], isHex: true);

            case IndirectReferenceToken reference:
                return getReference(reference.Data);

            case ArrayToken array:
                {
                    var result = new PdfArray(owner);

                    foreach (var element in array.Data)
                    {
                        result.Elements.Add(FromToken(element, owner, getReference, depth + 1));
                    }

                    return result;
                }

            case DictionaryToken dictionary:
                {
                    var result = new PdfDictionary(owner);

                    CopyEntries(dictionary, result, owner, getReference, depth);

                    return result;
                }

            case StreamToken stream:
                {
                    var result = new PdfDictionary(owner);

                    CopyEntries(stream.StreamDictionary, result, owner, getReference, depth);

                    // The writer gives every stream the length of its data, so a stored one is not kept.
                    result.Elements.Remove("/Length");
                    result.CreateStream(stream.Data.ToArray());

                    return result;
                }

            default:
                return PdfNull.Value;
        }
    }

    /// <summary>
    /// Copies a dictionary token's entries into a dictionary of the model.
    /// </summary>
    /// <param name="source">The token.</param>
    /// <param name="target">The dictionary.</param>
    /// <param name="owner">The document the objects belong to.</param>
    /// <param name="getReference">Returns the document's reference for an object number and generation.</param>
    /// <param name="depth">How deep the conversion is.</param>
    private static void CopyEntries(DictionaryToken source, PdfDictionary target, PdfDocument owner, Func<IndirectReference, PdfReference> getReference, int depth)
    {
        foreach (var (key, value) in source.Data)
        {
            target.Elements["/" + key] = FromToken(value, owner, getReference, depth + 1);
        }
    }

    private static IToken ToToken(PdfItem item, int depth)
    {
        if (depth > MaxDepth)
        {
            return NullToken.Instance;
        }

        return Resolve(item) switch
        {
            PdfBoolean boolean => boolean.Value
                ? BooleanToken.True
                : BooleanToken.False,
            PdfInteger number => new NumericToken(number.Value),
            PdfReal number => new NumericToken(number.Value),
            PdfName name => NameToken.Create(name.Value[1..]),
            PdfString text => new HexToken(Convert.ToHexString(text.Bytes).AsSpan()),
            PdfArray array => new ArrayToken([.. array.Elements.Select(element => ToToken(element, depth + 1))]),
            PdfDictionary dictionary => new DictionaryToken(dictionary.Elements.ToDictionary(
                entry => NameToken.Create(entry.Key[1..]),
                entry => ToToken(entry.Value, depth + 1))),
            PdfRectangle rectangle => new ArrayToken(
            [
                new NumericToken(rectangle.X1),
                new NumericToken(rectangle.Y1),
                new NumericToken(rectangle.X2),
                new NumericToken(rectangle.Y2),
            ]),
            _ => NullToken.Instance,
        };
    }

    /// <summary>
    /// Formats a number the way the writer does: invariant, without an exponent, to five places.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The text.</returns>
    public static string Format(double value)
    {
        if (!double.IsFinite(value))
        {
            return "0";
        }

        var rounded = Math.Round(value, 5);

        return rounded == 0
            ? "0"
            : rounded.ToString("0.#####", CultureInfo.InvariantCulture);
    }
}
