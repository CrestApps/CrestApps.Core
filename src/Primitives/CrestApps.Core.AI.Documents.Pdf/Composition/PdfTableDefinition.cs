namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// A table: its columns, its rows as text, and how it is presented.
/// </summary>
internal sealed class PdfTableDefinition
{
    /// <summary>
    /// Gets or sets the columns, in order.
    /// </summary>
    public List<PdfTableColumnDefinition> Columns { get; set; } = [];

    /// <summary>
    /// Gets or sets the rows. Each row holds one value per column, as text.
    /// </summary>
    public List<List<string>> Rows { get; set; } = [];

    /// <summary>
    /// Gets or sets a caption printed under the table.
    /// </summary>
    public string Caption { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether alternating rows are shaded.
    /// </summary>
    public bool? Banded { get; set; }

    /// <summary>
    /// Gets or sets the header row fill colour.
    /// </summary>
    public string HeaderBackground { get; set; }

    /// <summary>
    /// Gets or sets the header row text colour.
    /// </summary>
    public string HeaderTextColor { get; set; }

    /// <summary>
    /// Gets or sets the fill colour of the shaded rows.
    /// </summary>
    public string BandColor { get; set; }

    /// <summary>
    /// Gets or sets the border colour.
    /// </summary>
    public string BorderColor { get; set; }

    /// <summary>
    /// Gets or sets which borders are drawn: <c>all</c>, <c>horizontal</c> or <c>none</c>.
    /// </summary>
    public string Borders { get; set; }

    /// <summary>
    /// Gets or sets the font size in points.
    /// </summary>
    public double? FontSize { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the header row is repeated at the top of every page the table
    /// runs onto. Defaults to <see langword="true"/>.
    /// </summary>
    public bool? RepeatHeader { get; set; }

    /// <summary>
    /// Gets or sets the total row appended below the data.
    /// </summary>
    public PdfTotalRowDefinition TotalRow { get; set; }

    /// <summary>
    /// Gets or sets the rules that colour cells by their value.
    /// </summary>
    public List<PdfHighlightRuleDefinition> HighlightRules { get; set; }

    /// <summary>
    /// Gets or sets a description of where the rows were read from, kept so a follow-up can tell the reader
    /// what the table shows.
    /// </summary>
    public string SourceDescription { get; set; }

    /// <summary>
    /// Gets or sets how many rows the source held when more were available than the table shows.
    /// </summary>
    public long? SourceRowCount { get; set; }
}
