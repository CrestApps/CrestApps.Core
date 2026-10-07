using System.Globalization;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// Reads PDFsharp's low-level objects — dictionaries, arrays, names and strings — following indirect
/// references, so the document-property tools can walk structures PDFsharp has no typed model for.
/// </summary>
internal static class PdfObjects
{
    /// <summary>
    /// Follows an indirect reference to the object it points at.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The direct item, or <see langword="null"/>.</returns>
    public static PdfItem Resolve(PdfItem item)
    {
        var current = item;

        for (var depth = 0; depth < 8 && current is PdfReference reference; depth++)
        {
            current = reference.Value;
        }

        return current is PdfReference ? null : current;
    }

    /// <summary>
    /// Reads an entry of a dictionary as a dictionary.
    /// </summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The key, with its slash.</param>
    /// <returns>The dictionary, or <see langword="null"/>.</returns>
    public static PdfDictionary GetDictionary(PdfDictionary dictionary, string key)
    {
        if (dictionary is null)
        {
            return null;
        }

        return Resolve(dictionary.Elements[key]) as PdfDictionary;
    }

    /// <summary>
    /// Reads an entry of a dictionary as an array.
    /// </summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The key, with its slash.</param>
    /// <returns>The array, or <see langword="null"/>.</returns>
    public static PdfArray GetArray(PdfDictionary dictionary, string key)
    {
        if (dictionary is null)
        {
            return null;
        }

        return Resolve(dictionary.Elements[key]) as PdfArray;
    }

    /// <summary>
    /// Reads an entry of a dictionary as text, from a string, a name or a number.
    /// </summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The key, with its slash.</param>
    /// <returns>The text, or <see langword="null"/> when the entry is missing or empty.</returns>
    public static string GetText(PdfDictionary dictionary, string key)
    {
        if (dictionary is null)
        {
            return null;
        }

        return ToText(Resolve(dictionary.Elements[key]));
    }

    /// <summary>
    /// Writes a simple item as text.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The text, or <see langword="null"/> when the item is not a simple value or is empty.</returns>
    public static string ToText(PdfItem item)
    {
        var text = item switch
        {
            PdfString value => value.Value,
            PdfName name => name.Value.TrimStart('/'),
            PdfDate date => date.ToString(),
            PdfInteger number => number.Value.ToString(CultureInfo.InvariantCulture),
            PdfLongInteger number => number.Value.ToString(CultureInfo.InvariantCulture),
            PdfReal number => number.Value.ToString(CultureInfo.InvariantCulture),
            PdfBoolean flag => flag.Value ? "true" : "false",
            _ => null,
        };

        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>
    /// Reads an entry of a dictionary as a name, without its slash.
    /// </summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The key, with its slash.</param>
    /// <returns>The name, or <see langword="null"/>.</returns>
    public static string GetName(PdfDictionary dictionary, string key)
    {
        if (dictionary is null)
        {
            return null;
        }

        return Resolve(dictionary.Elements[key]) is PdfName name
            ? name.Value.TrimStart('/')
            : null;
    }

    /// <summary>
    /// Reads an entry of a dictionary as a whole number.
    /// </summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The key, with its slash.</param>
    /// <returns>The number, or <see langword="null"/>.</returns>
    public static long? GetInteger(PdfDictionary dictionary, string key)
    {
        if (dictionary is null)
        {
            return null;
        }

        return Resolve(dictionary.Elements[key]) switch
        {
            PdfInteger number => number.Value,
            PdfLongInteger number => number.Value,
            PdfReal number => (long)number.Value,
            _ => null,
        };
    }

    /// <summary>
    /// Reads an item that may be a single value or an array of values as a list.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The values, unresolved.</returns>
    public static List<PdfItem> Items(PdfItem item)
    {
        var items = new List<PdfItem>();

        switch (Resolve(item))
        {
            case null:
                break;
            case PdfArray array:
                items.AddRange(array.Elements);

                break;
            default:
                items.Add(item);

                break;
        }

        return items;
    }

    /// <summary>
    /// Returns the indirect object an item stands for, whether it is a reference or the object itself.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The object, or <see langword="null"/>.</returns>
    public static PdfObject AsObject(PdfItem item)
    {
        return Resolve(item) as PdfObject;
    }

    /// <summary>
    /// Returns whether two items stand for the same object.
    /// </summary>
    /// <param name="first">The first item.</param>
    /// <param name="second">The second item.</param>
    /// <returns><see langword="true"/> when both resolve to the same object.</returns>
    public static bool SameObject(PdfItem first, PdfItem second)
    {
        var left = AsObject(first);
        var right = AsObject(second);

        return left is not null && ReferenceEquals(left, right);
    }

    /// <summary>
    /// Returns the digital signature fields of a document that carry a signature.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The number of signed signature fields.</returns>
    public static int CountSignatures(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var form = GetDictionary(document.Internals.Catalog, "/AcroForm");
        var fields = GetArray(form, "/Fields");

        if (fields is null)
        {
            return 0;
        }

        var count = 0;
        var pending = new Stack<(PdfItem Item, string InheritedType, int Depth)>();

        foreach (var field in fields.Elements)
        {
            pending.Push((field, null, 0));
        }

        var seen = new HashSet<PdfDictionary>();

        while (pending.Count > 0)
        {
            var (item, inheritedType, depth) = pending.Pop();

            if (Resolve(item) is not PdfDictionary field || !seen.Add(field) || depth > 32)
            {
                continue;
            }

            var type = GetName(field, "/FT") ?? inheritedType;
            var kids = GetArray(field, "/Kids");

            if (kids is not null)
            {
                foreach (var kid in kids.Elements)
                {
                    pending.Push((kid, type, depth + 1));
                }
            }

            if (string.Equals(type, "Sig", StringComparison.Ordinal) && GetDictionary(field, "/V") is not null)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Describes the effect an edit has on a document that was digitally signed.
    /// </summary>
    /// <param name="document">The document as it was opened.</param>
    /// <returns>A warning sentence, or <see langword="null"/> when the document carries no signature.</returns>
    public static string DescribeBrokenSignatures(PdfDocument document)
    {
        var count = CountSignatures(document);

        return count == 0
            ? null
            : $"The source carried {count} digital signature(s); saving any change rewrites the file, so the working copy's signatures no longer verify. Sign again with sign_pdf after the last edit if a signature is needed.";
    }
}
