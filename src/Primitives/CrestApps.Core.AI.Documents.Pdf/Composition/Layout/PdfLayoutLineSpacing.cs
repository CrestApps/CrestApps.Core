namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// How the height of a paragraph's lines is set.
/// </summary>
internal enum PdfLayoutLineSpacing
{
    /// <summary>
    /// The height the line's tallest font gives it.
    /// </summary>
    Single,

    /// <summary>
    /// A multiple of the single height.
    /// </summary>
    Multiple,

    /// <summary>
    /// Exactly a height, in points, whatever the fonts.
    /// </summary>
    Exactly,
}
