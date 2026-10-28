namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// Adds the clickable text of a link to a paragraph.
/// </summary>
internal sealed class PdfLayoutHyperlink
{
    private readonly PdfLayoutParagraph _paragraph;
    private readonly PdfLayoutLink _link;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfLayoutHyperlink"/> class.
    /// </summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <param name="link">Where the link goes.</param>
    public PdfLayoutHyperlink(PdfLayoutParagraph paragraph, PdfLayoutLink link)
    {
        _paragraph = paragraph;
        _link = link;
    }

    /// <summary>
    /// Adds clickable text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The run, to give a font of its own.</returns>
    public PdfLayoutText AddText(string text)
    {
        var run = _paragraph.AddText(text);

        run.Link = _link;

        return run;
    }

    /// <summary>
    /// Adds a clickable tab.
    /// </summary>
    public void AddTab()
    {
        _paragraph.Add(new PdfLayoutBreak(isTab: true)).Link = _link;
    }

    /// <summary>
    /// Adds the clickable number of the page a bookmark is on.
    /// </summary>
    /// <param name="bookmark">The bookmark's name.</param>
    /// <returns>The field, to give a number format.</returns>
    public PdfLayoutField AddPageRefField(string bookmark)
    {
        var field = _paragraph.Add(new PdfLayoutField(PdfLayoutFieldKind.PageReference, bookmark));

        field.Link = _link;

        return field;
    }
}
