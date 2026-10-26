namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// A run of text.
/// </summary>
internal sealed class PdfLayoutText : PdfLayoutInline
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfLayoutText"/> class.
    /// </summary>
    /// <param name="text">The text.</param>
    public PdfLayoutText(string text)
    {
        Text = text ?? string.Empty;
    }

    /// <summary>
    /// Gets the text.
    /// </summary>
    public string Text { get; }
}
