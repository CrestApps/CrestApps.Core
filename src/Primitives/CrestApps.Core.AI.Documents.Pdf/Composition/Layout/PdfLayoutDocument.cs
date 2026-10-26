namespace CrestApps.Core.AI.Documents.Pdf.Composition.Layout;

/// <summary>
/// A document to lay out into pages: its styles, its sections and what describes it.
/// </summary>
/// <remarks>
/// The model mirrors the MigraDoc document object model the composer used, so the composer builds a
/// document the way it did; <see cref="PdfLayoutRenderer"/> lays it out and writes the PDF.
/// </remarks>
internal sealed class PdfLayoutDocument
{
    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the author.
    /// </summary>
    public string Author { get; set; }

    /// <summary>
    /// Gets or sets the subject.
    /// </summary>
    public string Subject { get; set; }

    /// <summary>
    /// Gets or sets the keywords.
    /// </summary>
    public string Keywords { get; set; }

    /// <summary>
    /// Gets the paragraph styles.
    /// </summary>
    public PdfLayoutStyles Styles { get; } = new();

    /// <summary>
    /// Gets the sections.
    /// </summary>
    public List<PdfLayoutSection> Sections { get; } = [];

    /// <summary>
    /// Adds a section.
    /// </summary>
    /// <returns>The section.</returns>
    public PdfLayoutSection AddSection()
    {
        var section = new PdfLayoutSection();

        Sections.Add(section);

        return section;
    }
}
