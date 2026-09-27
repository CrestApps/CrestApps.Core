using PdfSharp.Pdf;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Maps the page objects of a document to their page numbers, so a destination or a structure element that
/// refers to a page can be turned into the number a reader knows.
/// </summary>
internal sealed class PdfPageIndex
{
    private readonly Dictionary<PdfDictionary, int> _byDictionary = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, int> _byObjectNumber = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfPageIndex"/> class.
    /// </summary>
    /// <param name="document">The document, opened with PDFsharp.</param>
    public PdfPageIndex(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        for (var index = 0; index < document.PageCount; index++)
        {
            var page = document.Pages[index];

            _byDictionary[page] = index + 1;

            var objectNumber = PdfObjects.ObjectNumber(page);

            if (objectNumber > 0)
            {
                _byObjectNumber.TryAdd(objectNumber, index + 1);
            }
        }
    }

    /// <summary>
    /// Finds the page an item refers to.
    /// </summary>
    /// <param name="item">The item: a page dictionary, or a reference to one.</param>
    /// <param name="page">The one-based page number.</param>
    /// <returns><see langword="true"/> when the item is a page of the document.</returns>
    public bool TryGetPage(PdfItem item, out int page)
    {
        if (PdfObjects.Resolve(item) is PdfDictionary dictionary && _byDictionary.TryGetValue(dictionary, out page))
        {
            return true;
        }

        return _byObjectNumber.TryGetValue(PdfObjects.ObjectNumber(item), out page);
    }
}
