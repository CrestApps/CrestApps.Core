namespace CrestApps.Core.AI.Documents.Pdf;

/// <summary>
/// The defaults every generated PDF starts from — the ones <c>generate_file</c>, a tabular export and the
/// PDF agent all share — before a document asks for anything else.
/// </summary>
public sealed class PdfCompositionOptions
{
    /// <summary>
    /// Gets or sets the paper size used when a document does not choose one: <c>A4</c>, <c>Letter</c>,
    /// <c>Legal</c>, <c>A3</c>, <c>A5</c>, <c>B5</c>, <c>Tabloid</c> or <c>Executive</c>. Defaults to <c>A4</c>.
    /// </summary>
    public string DefaultPageSize { get; set; } = "A4";

    /// <summary>
    /// Gets or sets the margin, in millimetres, used on every edge a document does not set. Defaults to 20.
    /// </summary>
    public double DefaultMarginMm { get; set; } = 20;

    /// <summary>
    /// Gets or sets the body font family used when a document does not choose one. Defaults to <c>Arial</c>.
    /// </summary>
    public string DefaultFontFamily { get; set; } = "Arial";

    /// <summary>
    /// Gets or sets the most rows a single table is laid out with. Rows past it are left out and the table
    /// says so, because a table of tens of thousands of rows is thousands of pages nobody reads.
    /// Defaults to 5,000.
    /// </summary>
    public int MaxTableRows { get; set; } = 5000;

    /// <summary>
    /// Gets or sets the most categories a single chart plots. Defaults to 60.
    /// </summary>
    public int MaxChartCategories { get; set; } = 60;
}
