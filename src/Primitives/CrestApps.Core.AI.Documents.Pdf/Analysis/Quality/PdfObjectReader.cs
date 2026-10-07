using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Reads PDFsharp's object model without changing it: follows references, reads typed values, walks every
/// dictionary of a file and looks names up in name trees.
/// </summary>
/// <remarks>
/// PDFsharp's convenience properties (<c>PdfDocument.Info</c>, <c>ViewerPreferences</c>, a page's
/// <c>Resources</c>) create what they do not find. A check must not do that — it would report what it just
/// added — so the quality tools read the raw dictionaries through here.
/// </remarks>
internal static class PdfObjectReader
{
    /// <summary>
    /// Follows references to the object they point at.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The direct object, or <see langword="null"/> when a reference points at nothing.</returns>
    public static PdfItem Resolve(PdfItem item)
    {
        for (var depth = 0; depth < 16 && item is PdfReference reference; depth++)
        {
            item = reference.Value;
        }

        return item is PdfReference
            ? null
            : item;
    }

    /// <summary>
    /// Reads an entry of a dictionary, following references.
    /// </summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The key, with its leading slash.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public static PdfItem Get(PdfDictionary dictionary, string key)
    {
        if (dictionary is null || !dictionary.Elements.TryGetValue(key, out var value))
        {
            return null;
        }

        return Resolve(value);
    }

    /// <summary>
    /// Reads an entry as a dictionary.
    /// </summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The key.</param>
    /// <returns>The dictionary, or <see langword="null"/>.</returns>
    public static PdfDictionary GetDictionary(PdfDictionary dictionary, string key)
    {
        return Get(dictionary, key) as PdfDictionary;
    }

    /// <summary>
    /// Reads an entry as an array.
    /// </summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The key.</param>
    /// <returns>The array, or <see langword="null"/>.</returns>
    public static PdfArray GetArray(PdfDictionary dictionary, string key)
    {
        return Get(dictionary, key) as PdfArray;
    }

    /// <summary>
    /// Reads an entry as a name.
    /// </summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The key.</param>
    /// <returns>The name with its leading slash, for example <c>/Type1</c>, or <see langword="null"/>.</returns>
    public static string GetName(PdfDictionary dictionary, string key)
    {
        return AsName(Get(dictionary, key));
    }

    /// <summary>
    /// Reads an item as a name.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The name with its leading slash, or <see langword="null"/>.</returns>
    public static string AsName(PdfItem item)
    {
        return Resolve(item) switch
        {
            PdfName name => name.Value,
            PdfNameObject name => name.Value,
            _ => null,
        };
    }

    /// <summary>
    /// Reads an entry as text: a string, or a name without its slash.
    /// </summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The key.</param>
    /// <returns>The text, or <see langword="null"/>.</returns>
    public static string GetText(PdfDictionary dictionary, string key)
    {
        return AsText(Get(dictionary, key));
    }

    /// <summary>
    /// Reads an item as text: a string, or a name without its slash.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The text, or <see langword="null"/>.</returns>
    public static string AsText(PdfItem item)
    {
        return Resolve(item) switch
        {
            PdfString text => text.Value,
            PdfStringObject text => text.Value,
            PdfName name => name.Value.TrimStart('/'),
            PdfNameObject name => name.Value.TrimStart('/'),
            _ => null,
        };
    }

    /// <summary>
    /// Reads an entry as a number.
    /// </summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The key.</param>
    /// <returns>The number, or <see langword="null"/>.</returns>
    public static double? GetNumber(PdfDictionary dictionary, string key)
    {
        return AsNumber(Get(dictionary, key));
    }

    /// <summary>
    /// Reads an item as a number.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The number, or <see langword="null"/>.</returns>
    public static double? AsNumber(PdfItem item)
    {
        return Resolve(item) switch
        {
            PdfInteger number => number.Value,
            PdfLongInteger number => number.Value,
            PdfReal number => number.Value,
            PdfIntegerObject number => number.Value,
            PdfRealObject number => number.Value,
            _ => null,
        };
    }

    /// <summary>
    /// Reads an entry as a boolean.
    /// </summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The key.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public static bool? GetBoolean(PdfDictionary dictionary, string key)
    {
        return Get(dictionary, key) switch
        {
            PdfBoolean value => value.Value,
            PdfBooleanObject value => value.Value,
            _ => null,
        };
    }

    /// <summary>
    /// Reads an entry as an array of numbers.
    /// </summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The key.</param>
    /// <returns>The numbers, or <see langword="null"/> when the entry is not an array of numbers.</returns>
    public static List<double> GetNumbers(PdfDictionary dictionary, string key)
    {
        return AsNumbers(Get(dictionary, key));
    }

    /// <summary>
    /// Reads an item as an array of numbers.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The numbers, or <see langword="null"/> when the item is not an array of numbers.</returns>
    public static List<double> AsNumbers(PdfItem item)
    {
        var resolved = Resolve(item);

        // PDFsharp reads page boxes into rectangles of its own.
        if (resolved is PdfRectangle rectangle)
        {
            return [rectangle.X1, rectangle.Y1, rectangle.X2, rectangle.Y2];
        }

        if (resolved is not PdfArray array)
        {
            return null;
        }

        var numbers = new List<double>(array.Elements.Count);

        foreach (var element in array.Elements)
        {
            var number = AsNumber(element);

            if (number is null)
            {
                return null;
            }

            numbers.Add(number.Value);
        }

        return numbers;
    }

    /// <summary>
    /// Returns whether an entry is a given name.
    /// </summary>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="key">The key.</param>
    /// <param name="name">The name, with its leading slash.</param>
    /// <returns><see langword="true"/> when the entry is that name.</returns>
    public static bool IsName(PdfDictionary dictionary, string key, string name)
    {
        return string.Equals(GetName(dictionary, key), name, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads a page attribute that may be inherited from the page tree, such as <c>/Resources</c> or
    /// <c>/MediaBox</c>.
    /// </summary>
    /// <param name="page">The page dictionary.</param>
    /// <param name="key">The key.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public static PdfItem GetInherited(PdfDictionary page, string key)
    {
        var current = page;

        for (var depth = 0; depth < 32 && current is not null; depth++)
        {
            if (current.Elements.ContainsKey(key))
            {
                return Get(current, key);
            }

            current = GetDictionary(current, "/Parent");
        }

        return null;
    }

    /// <summary>
    /// Lists the items of an array, following references.
    /// </summary>
    /// <param name="array">The array, or <see langword="null"/>.</param>
    /// <returns>The resolved items; references that point at nothing are left out.</returns>
    public static List<PdfItem> Items(PdfArray array)
    {
        var items = new List<PdfItem>();

        if (array is null)
        {
            return items;
        }

        foreach (var element in array.Elements)
        {
            var resolved = Resolve(element);

            if (resolved is not null)
            {
                items.Add(resolved);
            }
        }

        return items;
    }

    /// <summary>
    /// Lists every dictionary of a file: each indirect object and every dictionary nested in one.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="max">The most dictionaries returned.</param>
    /// <returns>The dictionaries, each once.</returns>
    public static List<PdfDictionary> EnumerateDictionaries(PdfDocument document, int max = 200_000)
    {
        ArgumentNullException.ThrowIfNull(document);

        var dictionaries = new List<PdfDictionary>();
        var seen = new HashSet<PdfItem>(ReferenceEqualityComparer.Instance);

        foreach (var root in document.Internals.GetAllObjects())
        {
            Collect(root, dictionaries, seen, 0, max);

            if (dictionaries.Count >= max)
            {
                break;
            }
        }

        return dictionaries;
    }

    /// <summary>
    /// Lists the references of a file that point at no object.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="max">The most references returned.</param>
    /// <returns>Each broken reference as <c>12 0 R</c>, with the key it was found under.</returns>
    public static List<string> FindBrokenReferences(PdfDocument document, int max = 50)
    {
        ArgumentNullException.ThrowIfNull(document);

        var broken = new List<string>();

        foreach (var dictionary in EnumerateDictionaries(document))
        {
            foreach (var pair in dictionary.Elements)
            {
                CheckReference(pair.Key, pair.Value, broken);

                if (pair.Value is PdfArray array)
                {
                    foreach (var element in array.Elements)
                    {
                        CheckReference(pair.Key, element, broken);
                    }
                }

                if (broken.Count >= max)
                {
                    return broken;
                }
            }
        }

        return broken;
    }

    /// <summary>
    /// Looks a name up in a name tree, such as the <c>/Dests</c> tree of the <c>/Names</c> dictionary.
    /// </summary>
    /// <param name="node">The tree's root node.</param>
    /// <param name="name">The name.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public static PdfItem LookUpNameTree(PdfDictionary node, string name)
    {
        return LookUpNameTree(node, name, 0);
    }

    /// <summary>
    /// Lists the values of a name tree.
    /// </summary>
    /// <param name="node">The tree's root node.</param>
    /// <param name="max">The most entries returned.</param>
    /// <returns>The keys and resolved values.</returns>
    public static List<(string Name, PdfItem Value)> ReadNameTree(PdfDictionary node, int max = 10_000)
    {
        var entries = new List<(string Name, PdfItem Value)>();

        ReadNameTree(node, entries, 0, max);

        return entries;
    }

    /// <summary>
    /// Gets the object number of an item, when it is an indirect object.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The object number, or 0.</returns>
    public static int ObjectNumber(PdfItem item)
    {
        return item switch
        {
            PdfReference reference => reference.ObjectNumber,
            PdfObject { Reference: not null } indirect => indirect.Reference.ObjectNumber,
            _ => 0,
        };
    }

    private static void Collect(PdfItem item, List<PdfDictionary> dictionaries, HashSet<PdfItem> seen, int depth, int max)
    {
        if (item is null || item is PdfReference || depth > 32 || dictionaries.Count >= max || !seen.Add(item))
        {
            return;
        }

        if (item is PdfDictionary dictionary)
        {
            dictionaries.Add(dictionary);

            foreach (var pair in dictionary.Elements)
            {
                Collect(pair.Value, dictionaries, seen, depth + 1, max);
            }

            return;
        }

        if (item is PdfArray array)
        {
            foreach (var element in array.Elements)
            {
                Collect(element, dictionaries, seen, depth + 1, max);
            }
        }
    }

    private static void CheckReference(string key, PdfItem item, List<string> broken)
    {
        if (item is PdfReference reference && reference.Value is null)
        {
            broken.Add($"{reference.ObjectNumber} {reference.GenerationNumber} R under {key}");
        }
    }

    private static PdfItem LookUpNameTree(PdfDictionary node, string name, int depth)
    {
        if (node is null || depth > 32)
        {
            return null;
        }

        var names = GetArray(node, "/Names");

        if (names is not null)
        {
            for (var index = 0; index + 1 < names.Elements.Count; index += 2)
            {
                if (string.Equals(AsText(names.Elements[index]), name, StringComparison.Ordinal))
                {
                    return Resolve(names.Elements[index + 1]);
                }
            }
        }

        foreach (var kid in Items(GetArray(node, "/Kids")))
        {
            if (kid is not PdfDictionary child)
            {
                continue;
            }

            var limits = GetArray(child, "/Limits");

            if (limits is { Elements.Count: 2 })
            {
                var low = AsText(limits.Elements[0]);
                var high = AsText(limits.Elements[1]);

                if (low is not null &&
                    high is not null &&
                    (string.CompareOrdinal(name, low) < 0 || string.CompareOrdinal(name, high) > 0))
                {
                    continue;
                }
            }

            var found = LookUpNameTree(child, name, depth + 1);

            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static void ReadNameTree(PdfDictionary node, List<(string Name, PdfItem Value)> entries, int depth, int max)
    {
        if (node is null || depth > 32 || entries.Count >= max)
        {
            return;
        }

        var names = GetArray(node, "/Names");

        if (names is not null)
        {
            for (var index = 0; index + 1 < names.Elements.Count && entries.Count < max; index += 2)
            {
                entries.Add((AsText(names.Elements[index]), Resolve(names.Elements[index + 1])));
            }
        }

        foreach (var kid in Items(GetArray(node, "/Kids")))
        {
            ReadNameTree(kid as PdfDictionary, entries, depth + 1, max);
        }
    }
}
