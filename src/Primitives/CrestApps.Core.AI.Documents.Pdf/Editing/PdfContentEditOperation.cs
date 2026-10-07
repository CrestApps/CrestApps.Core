namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// One step of an <c>edit_pdf_content</c> call.
/// </summary>
internal sealed class PdfContentEditOperation
{
    /// <summary>
    /// Gets or sets the operation: <c>replace_text</c>, <c>add_text</c>, <c>add_image</c>,
    /// <c>remove_area</c> or <c>cover</c>.
    /// </summary>
    public string Operation { get; set; }

    /// <summary>
    /// Gets or sets the text a replace looks for.
    /// </summary>
    public string Find { get; set; }

    /// <summary>
    /// Gets or sets the text a replace writes in its place.
    /// </summary>
    public string Replace { get; set; }

    /// <summary>
    /// Gets or sets the pages a replace looks on.
    /// </summary>
    public string Pages { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a replace matches case.
    /// </summary>
    public bool? MatchCase { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a replace matches whole words only.
    /// </summary>
    public bool? WholeWord { get; set; }

    /// <summary>
    /// Gets or sets which occurrence a replace changes, one-based; every one when unset.
    /// </summary>
    public int? Occurrence { get; set; }

    /// <summary>
    /// Gets or sets the one-based page an addition or area is on.
    /// </summary>
    public int? Page { get; set; }

    /// <summary>
    /// Gets or sets the distance from the left edge, in points.
    /// </summary>
    public double? X { get; set; }

    /// <summary>
    /// Gets or sets the distance from the top edge, in points.
    /// </summary>
    public double? Y { get; set; }

    /// <summary>
    /// Gets or sets the width, in points.
    /// </summary>
    public double? Width { get; set; }

    /// <summary>
    /// Gets or sets the height, in points.
    /// </summary>
    public double? Height { get; set; }

    /// <summary>
    /// Gets or sets the text an add_text writes.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Gets or sets the image source an add_image places.
    /// </summary>
    public string Source { get; set; }

    /// <summary>
    /// Gets or sets the font size, in points.
    /// </summary>
    public double? FontSize { get; set; }

    /// <summary>
    /// Gets or sets the font family.
    /// </summary>
    public string FontFamily { get; set; }

    /// <summary>
    /// Gets or sets the text or fill colour.
    /// </summary>
    public string Color { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether text is bold.
    /// </summary>
    public bool? Bold { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether text is italic.
    /// </summary>
    public bool? Italic { get; set; }

    /// <summary>
    /// Gets or sets the alignment of wrapped text: <c>left</c>, <c>center</c> or <c>right</c>.
    /// </summary>
    public string Align { get; set; }

    /// <summary>
    /// Gets or sets what a removed area is filled with: <c>none</c> (default), <c>white</c>, or a colour.
    /// </summary>
    public string Fill { get; set; }
}
