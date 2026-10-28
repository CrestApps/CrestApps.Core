namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// Where a bookmark, or a heading in the outline, landed.
/// </summary>
/// <param name="PageIndex">The zero-based index of the page in the document.</param>
/// <param name="PageNumber">The number the page shows.</param>
/// <param name="Top">How far from the page's top edge it is, in points.</param>
internal readonly record struct PdfLayoutTarget(int PageIndex, int PageNumber, double Top);
