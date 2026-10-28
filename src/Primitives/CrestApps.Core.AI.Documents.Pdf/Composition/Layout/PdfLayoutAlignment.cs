namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// How lines of text, or text after a tab, are aligned.
/// </summary>
internal enum PdfLayoutAlignment
{
    /// <summary>
    /// Against the left edge.
    /// </summary>
    Left,

    /// <summary>
    /// Centred.
    /// </summary>
    Center,

    /// <summary>
    /// Against the right edge.
    /// </summary>
    Right,

    /// <summary>
    /// Against both edges, the last line of a paragraph against the left.
    /// </summary>
    Justify,
}
