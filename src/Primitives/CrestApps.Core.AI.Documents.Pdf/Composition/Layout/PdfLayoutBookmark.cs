namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// A named place in a paragraph that links and page references point at.
/// </summary>
internal sealed class PdfLayoutBookmark : PdfLayoutInline
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfLayoutBookmark"/> class.
    /// </summary>
    /// <param name="name">The bookmark's name.</param>
    public PdfLayoutBookmark(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Gets the bookmark's name.
    /// </summary>
    public string Name { get; }
}
