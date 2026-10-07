namespace CrestApps.Core.AI.Documents.Pdf.Rendering;

/// <summary>
/// One page drawn as a picture.
/// </summary>
/// <param name="PageNumber">The one-based page number.</param>
/// <param name="Svg">The SVG markup.</param>
/// <param name="Width">The page width as displayed, in points.</param>
/// <param name="Height">The page height as displayed, in points.</param>
/// <param name="ImagesDrawn">How many embedded pictures were drawn.</param>
/// <param name="ImagesSkipped">How many embedded pictures were drawn as placeholders instead.</param>
/// <param name="Simplified">Whether vector artwork was cut short because the page held more than the preview draws.</param>
internal sealed record PdfPageRendering(
    int PageNumber,
    string Svg,
    double Width,
    double Height,
    int ImagesDrawn,
    int ImagesSkipped,
    bool Simplified);
