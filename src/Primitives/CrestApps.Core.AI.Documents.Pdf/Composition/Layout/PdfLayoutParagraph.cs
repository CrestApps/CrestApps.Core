using CrestApps.Core.AI.Documents.Pdf.Graphics.Images;

namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// A paragraph: text, breaks, tabs, pictures and fields, set in lines that wrap to the width available.
/// </summary>
internal sealed class PdfLayoutParagraph : PdfLayoutBlock
{
    /// <summary>
    /// Gets or sets the style, or <see langword="null"/> for the normal one.
    /// </summary>
    public string Style { get; set; }

    /// <summary>
    /// Gets the format the paragraph sets over its style.
    /// </summary>
    public PdfLayoutParagraphFormat Format { get; } = new();

    /// <summary>
    /// Gets what the paragraph holds, in order.
    /// </summary>
    public List<PdfLayoutInline> Inlines { get; } = [];

    /// <summary>
    /// Adds text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The run, to give a font of its own.</returns>
    public PdfLayoutText AddText(string text)
    {
        return Add(new PdfLayoutText(text));
    }

    /// <summary>
    /// Adds a number of spaces.
    /// </summary>
    /// <param name="count">How many.</param>
    public void AddSpace(int count)
    {
        AddText(new string(' ', Math.Max(1, count)));
    }

    /// <summary>
    /// Adds a line break.
    /// </summary>
    public void AddLineBreak()
    {
        Add(new PdfLayoutBreak(isTab: false));
    }

    /// <summary>
    /// Adds a tab.
    /// </summary>
    public void AddTab()
    {
        Add(new PdfLayoutBreak(isTab: true));
    }

    /// <summary>
    /// Adds a picture.
    /// </summary>
    /// <param name="image">The picture.</param>
    /// <param name="width">The width, in points.</param>
    /// <param name="height">The height, in points.</param>
    public void AddImage(PdfImage image, double width, double height)
    {
        Add(new PdfLayoutPicture(image, width, height));
    }

    /// <summary>
    /// Adds a bookmark where the paragraph is.
    /// </summary>
    /// <param name="name">The bookmark's name.</param>
    public void AddBookmark(string name)
    {
        Add(new PdfLayoutBookmark(name));
    }

    /// <summary>
    /// Adds the number of the page.
    /// </summary>
    /// <returns>The field, to give a number format.</returns>
    public PdfLayoutField AddPageField()
    {
        return Add(new PdfLayoutField(PdfLayoutFieldKind.Page));
    }

    /// <summary>
    /// Adds the number of pages in the document.
    /// </summary>
    /// <returns>The field, to give a number format.</returns>
    public PdfLayoutField AddNumPagesField()
    {
        return Add(new PdfLayoutField(PdfLayoutFieldKind.NumPages));
    }

    /// <summary>
    /// Adds the number of pages in the section.
    /// </summary>
    /// <returns>The field, to give a number format.</returns>
    public PdfLayoutField AddSectionPagesField()
    {
        return Add(new PdfLayoutField(PdfLayoutFieldKind.SectionPages));
    }

    /// <summary>
    /// Starts a link: what is added through it is the clickable text.
    /// </summary>
    /// <param name="target">The web address, or the bookmark's name.</param>
    /// <param name="isWeb">Whether the target is a web address rather than a bookmark.</param>
    /// <returns>The link.</returns>
    public PdfLayoutHyperlink AddHyperlink(string target, bool isWeb)
    {
        return new PdfLayoutHyperlink(this, new PdfLayoutLink(target, isWeb));
    }

    /// <summary>
    /// Adds a piece to the paragraph.
    /// </summary>
    /// <typeparam name="T">The kind of piece.</typeparam>
    /// <param name="inline">The piece.</param>
    /// <returns>The piece.</returns>
    public T Add<T>(T inline)
        where T : PdfLayoutInline
    {
        Inlines.Add(inline);

        return inline;
    }
}
