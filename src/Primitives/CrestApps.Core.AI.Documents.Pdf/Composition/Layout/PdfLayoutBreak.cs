namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// A line break or a tab inside a paragraph.
/// </summary>
internal sealed class PdfLayoutBreak : PdfLayoutInline
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfLayoutBreak"/> class.
    /// </summary>
    /// <param name="isTab">Whether it is a tab rather than a line break.</param>
    public PdfLayoutBreak(bool isTab)
    {
        IsTab = isTab;
    }

    /// <summary>
    /// Gets a value indicating whether it is a tab rather than a line break.
    /// </summary>
    public bool IsTab { get; }
}
