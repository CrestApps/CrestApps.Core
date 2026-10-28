namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// A part of a document that starts on a new page, with its own page setup, headers and footers.
/// </summary>
internal sealed class PdfLayoutSection
{
    /// <summary>
    /// Gets the page size and margins.
    /// </summary>
    public PdfLayoutPageSetup PageSetup { get; } = new();

    /// <summary>
    /// Gets the headers.
    /// </summary>
    public PdfLayoutHeadersFooters Headers { get; } = new();

    /// <summary>
    /// Gets the footers.
    /// </summary>
    public PdfLayoutHeadersFooters Footers { get; } = new();

    /// <summary>
    /// Gets the content, in order.
    /// </summary>
    public List<PdfLayoutBlock> Blocks { get; } = [];

    /// <summary>
    /// Adds a paragraph.
    /// </summary>
    /// <param name="text">Text the paragraph starts with, or <see langword="null"/>.</param>
    /// <returns>The paragraph.</returns>
    public PdfLayoutParagraph AddParagraph(string text = null)
    {
        var paragraph = new PdfLayoutParagraph();

        if (text is not null)
        {
            paragraph.AddText(text);
        }

        Blocks.Add(paragraph);

        return paragraph;
    }

    /// <summary>
    /// Adds a table.
    /// </summary>
    /// <returns>The table.</returns>
    public PdfLayoutTable AddTable()
    {
        return Add(new PdfLayoutTable());
    }

    /// <summary>
    /// Adds a frame placed on the page.
    /// </summary>
    /// <returns>The frame.</returns>
    public PdfLayoutFrame AddFrame()
    {
        return Add(new PdfLayoutFrame());
    }

    /// <summary>
    /// Adds a page break.
    /// </summary>
    public void AddPageBreak()
    {
        Add(new PdfLayoutPageBreak());
    }

    /// <summary>
    /// Adds a block.
    /// </summary>
    /// <typeparam name="T">The kind of block.</typeparam>
    /// <param name="block">The block.</param>
    /// <returns>The block.</returns>
    public T Add<T>(T block)
        where T : PdfLayoutBlock
    {
        Blocks.Add(block);

        return block;
    }
}
