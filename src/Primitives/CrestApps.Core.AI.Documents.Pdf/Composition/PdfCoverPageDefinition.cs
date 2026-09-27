namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// A cover page printed before the table of contents and the body. It carries no running head or foot and
/// is not counted by the body's page numbers.
/// </summary>
internal sealed class PdfCoverPageDefinition
{
    /// <summary>
    /// Gets or sets a value indicating whether the cover page is printed.
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>
    /// Gets or sets the title printed on the cover. Defaults to the document title.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the line printed under the title.
    /// </summary>
    public string Subtitle { get; set; }

    /// <summary>
    /// Gets or sets the author line. Defaults to the document author.
    /// </summary>
    public string Author { get; set; }

    /// <summary>
    /// Gets or sets the date line, printed as given.
    /// </summary>
    public string Date { get; set; }

    /// <summary>
    /// Gets or sets a short note printed at the foot of the cover, such as a confidentiality statement.
    /// </summary>
    public string Note { get; set; }

    /// <summary>
    /// Gets or sets the image printed above the title. Defaults to the theme logo.
    /// </summary>
    public string Logo { get; set; }

    /// <summary>
    /// Gets or sets the colour the whole cover is filled with.
    /// </summary>
    public string BackgroundColor { get; set; }

    /// <summary>
    /// Gets or sets the colour of the cover text. Defaults to white on a filled cover.
    /// </summary>
    public string TextColor { get; set; }

    /// <summary>
    /// Gets or sets how the cover text is aligned: <c>left</c> or <c>center</c>.
    /// </summary>
    public string Align { get; set; }
}
