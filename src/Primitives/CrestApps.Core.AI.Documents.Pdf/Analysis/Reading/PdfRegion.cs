namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// A region of a page that is not running text: a table, a placed picture or a drawing.
/// </summary>
internal sealed class PdfRegion
{
    /// <summary>
    /// The kind of a table.
    /// </summary>
    public const string TableKind = "table";

    /// <summary>
    /// The kind of a placed picture.
    /// </summary>
    public const string ImageKind = "image";

    /// <summary>
    /// The kind of a drawing made of vector paths, such as a chart.
    /// </summary>
    public const string DrawingKind = "drawing";

    /// <summary>
    /// Gets the kind: <see cref="TableKind"/>, <see cref="ImageKind"/> or <see cref="DrawingKind"/>.
    /// </summary>
    public string Kind { get; init; }

    /// <summary>
    /// Gets the one-based page the region is on.
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// Gets where the region is, in user space, when the reader recorded it.
    /// </summary>
    public PdfBox? Box { get; init; }

    /// <summary>
    /// Gets a table's cells, row by row.
    /// </summary>
    public List<List<string>> Rows { get; init; }

    /// <summary>
    /// Gets a picture's width in pixels.
    /// </summary>
    public int PixelWidth { get; init; }

    /// <summary>
    /// Gets a picture's height in pixels.
    /// </summary>
    public int PixelHeight { get; init; }

    /// <summary>
    /// Gets or sets the picture, encoded in a format a browser shows, when it was read.
    /// </summary>
    public byte[] Content { get; set; }

    /// <summary>
    /// Gets or sets the media type of <see cref="Content"/>.
    /// </summary>
    public string MediaType { get; set; }

    /// <summary>
    /// Gets or sets the caption that describes the region, when one was found next to it.
    /// </summary>
    public string Caption { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the region is page furniture, such as a logo in the running
    /// head, rather than content.
    /// </summary>
    public bool IsDecoration { get; set; }

    /// <summary>
    /// Gets the number of columns a table has.
    /// </summary>
    public int ColumnCount => Rows is { Count: > 0 } ? Rows.Max(row => row.Count) : 0;
}
