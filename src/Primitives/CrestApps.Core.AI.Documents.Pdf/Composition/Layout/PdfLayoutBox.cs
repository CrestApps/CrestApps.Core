namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// An area of a page, in points from its top-left corner.
/// </summary>
/// <param name="Left">The left edge.</param>
/// <param name="Top">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
internal readonly record struct PdfLayoutBox(double Left, double Top, double Width, double Height);
