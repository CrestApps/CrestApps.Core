using CrestApps.Core.AI.Documents.OpenXml.Word;
using DocumentFormat.OpenXml;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Editing;

/// <summary>
/// Hands out drawing ids. Every drawing in a document — in its body, headers and footers — needs a different
/// one, and Word repairs or rejects a document where two share an id.
/// </summary>
internal sealed class WordDrawingIds
{
    private uint _next;

    private WordDrawingIds(uint next)
    {
        _next = next;
    }

    /// <summary>
    /// Reads the ids a document uses.
    /// </summary>
    /// <param name="package">The document.</param>
    /// <returns>The id source, continuing after the largest id in use.</returns>
    public static WordDrawingIds For(WordPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var roots = new List<OpenXmlElement> { package.MainPart.Document };

        roots.AddRange(package.MainPart.HeaderParts.Select(part => (OpenXmlElement)part.Header).Where(root => root is not null));
        roots.AddRange(package.MainPart.FooterParts.Select(part => (OpenXmlElement)part.Footer).Where(root => root is not null));

        var largest = roots
            .SelectMany(root => root.Descendants<DW.DocProperties>())
            .Select(properties => properties.Id?.Value ?? 0U)
            .DefaultIfEmpty(0U)
            .Max();

        return new WordDrawingIds(largest + 1);
    }

    /// <summary>
    /// Returns the next free id.
    /// </summary>
    /// <returns>The id.</returns>
    public uint Next()
    {
        return _next++;
    }
}
