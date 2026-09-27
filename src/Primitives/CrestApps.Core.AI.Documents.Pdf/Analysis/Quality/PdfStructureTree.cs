using PdfSharp.Pdf;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Reads the logical structure of a tagged PDF — its structure tree — in document order.
/// </summary>
internal static class PdfStructureTree
{
    private const int MaxElements = 100_000;

    /// <summary>
    /// Lists the structure elements of a document, depth first in document order.
    /// </summary>
    /// <param name="document">The document, opened with PDFsharp.</param>
    /// <returns>The elements, or an empty list when the document has no structure tree.</returns>
    public static List<PdfTagElement> Read(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var elements = new List<PdfTagElement>();
        var root = PdfObjectReader.GetDictionary(document.Internals.Catalog, "/StructTreeRoot");

        if (root is null)
        {
            return elements;
        }

        var roleMap = PdfObjectReader.GetDictionary(root, "/RoleMap");
        var visited = new HashSet<PdfItem>(ReferenceEqualityComparer.Instance);

        Walk(PdfObjectReader.Get(root, "/K"), roleMap, elements, visited, 0);

        return elements;
    }

    /// <summary>
    /// Maps a structure type to the standard type it stands for, following the role map.
    /// </summary>
    /// <param name="type">The type, without its slash.</param>
    /// <param name="roleMap">The role map, or <see langword="null"/>.</param>
    /// <returns>The standard type.</returns>
    public static string MapRole(string type, PdfDictionary roleMap)
    {
        var current = type;

        for (var step = 0; step < 8 && roleMap is not null && current is not null; step++)
        {
            var mapped = PdfObjectReader.GetName(roleMap, "/" + current)?.TrimStart('/');

            if (mapped is null || string.Equals(mapped, current, StringComparison.Ordinal))
            {
                break;
            }

            current = mapped;
        }

        return current;
    }

    private static void Walk(PdfItem item, PdfDictionary roleMap, List<PdfTagElement> elements, HashSet<PdfItem> visited, int depth)
    {
        if (item is null || depth > 256 || elements.Count >= MaxElements)
        {
            return;
        }

        if (item is PdfArray array)
        {
            foreach (var child in PdfObjectReader.Items(array))
            {
                Walk(child, roleMap, elements, visited, depth + 1);
            }

            return;
        }

        if (item is not PdfDictionary dictionary || !visited.Add(dictionary))
        {
            return;
        }

        // Marked-content and object references point at content, not at further structure.
        if (PdfObjectReader.IsName(dictionary, "/Type", "/MCR") || PdfObjectReader.IsName(dictionary, "/Type", "/OBJR"))
        {
            return;
        }

        var type = PdfObjectReader.GetName(dictionary, "/S")?.TrimStart('/');

        if (type is not null)
        {
            elements.Add(new PdfTagElement(dictionary, type, MapRole(type, roleMap)));
        }

        Walk(PdfObjectReader.Get(dictionary, "/K"), roleMap, elements, visited, depth + 1);
    }
}
