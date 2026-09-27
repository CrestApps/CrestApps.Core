using PdfSharp.Pdf;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// One element of a tagged PDF's structure tree.
/// </summary>
/// <param name="Dictionary">The structure element dictionary.</param>
/// <param name="Type">The element type as written, without its slash, for example <c>Figure</c>.</param>
/// <param name="StandardType">The standard type it maps to through the role map, for example <c>Figure</c> for a custom <c>Chart</c>.</param>
internal sealed record PdfStructureElement(PdfDictionary Dictionary, string Type, string StandardType)
{
    /// <summary>
    /// Gets the alternative text, or <see langword="null"/>.
    /// </summary>
    public string Alt => PdfObjects.GetText(Dictionary, "/Alt");

    /// <summary>
    /// Gets the replacement text, or <see langword="null"/>.
    /// </summary>
    public string ActualText => PdfObjects.GetText(Dictionary, "/ActualText");

    /// <summary>
    /// Gets a value indicating whether the element is a heading: <c>H</c> or <c>H1</c> to <c>H6</c>.
    /// </summary>
    public bool IsHeading => StandardType is "H" or "H1" or "H2" or "H3" or "H4" or "H5" or "H6";
}

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
    public static List<PdfStructureElement> Read(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var elements = new List<PdfStructureElement>();
        var root = PdfObjects.GetDictionary(document.Internals.Catalog, "/StructTreeRoot");

        if (root is null)
        {
            return elements;
        }

        var roleMap = PdfObjects.GetDictionary(root, "/RoleMap");
        var visited = new HashSet<PdfItem>(ReferenceEqualityComparer.Instance);

        Walk(PdfObjects.Get(root, "/K"), roleMap, elements, visited, 0);

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
            var mapped = PdfObjects.GetName(roleMap, "/" + current)?.TrimStart('/');

            if (mapped is null || string.Equals(mapped, current, StringComparison.Ordinal))
            {
                break;
            }

            current = mapped;
        }

        return current;
    }

    private static void Walk(PdfItem item, PdfDictionary roleMap, List<PdfStructureElement> elements, HashSet<PdfItem> visited, int depth)
    {
        if (item is null || depth > 256 || elements.Count >= MaxElements)
        {
            return;
        }

        if (item is PdfArray array)
        {
            foreach (var child in PdfObjects.Items(array))
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
        if (PdfObjects.IsName(dictionary, "/Type", "/MCR") || PdfObjects.IsName(dictionary, "/Type", "/OBJR"))
        {
            return;
        }

        var type = PdfObjects.GetName(dictionary, "/S")?.TrimStart('/');

        if (type is not null)
        {
            elements.Add(new PdfStructureElement(dictionary, type, MapRole(type, roleMap)));
        }

        Walk(PdfObjects.Get(dictionary, "/K"), roleMap, elements, visited, depth + 1);
    }
}
