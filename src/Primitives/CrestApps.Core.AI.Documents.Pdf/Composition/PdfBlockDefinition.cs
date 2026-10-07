namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// One piece of content in a composed document: a heading, a paragraph, a table, a chart, an image, and so
/// on. Which properties apply is decided by <see cref="Type"/>; the rest are ignored.
/// </summary>
/// <remarks>
/// A block is flat on purpose. It is written by a model one tool call at a time, and a shape where
/// <c>{"type": "heading", "text": "Summary"}</c> is a complete block is the shape a model gets right first
/// time. Every block carries an identifier so a later turn can replace or remove it by name.
/// </remarks>
internal sealed class PdfBlockDefinition
{
    /// <summary>
    /// Gets or sets the identifier the block is addressed by, for example <c>b7</c>.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Gets or sets the block type. See <see cref="PdfBlockTypes"/>.
    /// </summary>
    public string Type { get; set; }

    /// <summary>
    /// Gets or sets the text of a heading, paragraph, quote, code, callout or Markdown block. Paragraphs,
    /// headings, quotes and callouts accept inline Markdown: <c>**bold**</c>, <c>*italic*</c>,
    /// <c>`code`</c> and <c>[links](https://…)</c>.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Gets or sets the heading level, from 1 (largest) to 4.
    /// </summary>
    public int? Level { get; set; }

    /// <summary>
    /// Gets or sets the entries of a list, key-value or signature block.
    /// </summary>
    public List<string> Items { get; set; }

    /// <summary>
    /// Gets or sets the label/value pairs of a key-value block.
    /// </summary>
    public List<List<string>> Pairs { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a list is numbered.
    /// </summary>
    public bool? Ordered { get; set; }

    /// <summary>
    /// Gets or sets the title of a callout block.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the callout variant: <c>info</c>, <c>success</c>, <c>warning</c>, <c>danger</c> or
    /// <c>note</c>.
    /// </summary>
    public string Variant { get; set; }

    /// <summary>
    /// Gets or sets the table a table block draws.
    /// </summary>
    public PdfTableDefinition Table { get; set; }

    /// <summary>
    /// Gets or sets the image an image block draws.
    /// </summary>
    public PdfImageDefinition Image { get; set; }

    /// <summary>
    /// Gets or sets the chart a chart block draws.
    /// </summary>
    public PdfChartDefinition Chart { get; set; }

    /// <summary>
    /// Gets or sets the height of a spacer block, in points.
    /// </summary>
    public double? Height { get; set; }

    /// <summary>
    /// Gets or sets the horizontal alignment: <c>left</c>, <c>center</c>, <c>right</c> or <c>justify</c>.
    /// </summary>
    public string Align { get; set; }

    /// <summary>
    /// Gets or sets the font size in points, overriding the theme.
    /// </summary>
    public double? FontSize { get; set; }

    /// <summary>
    /// Gets or sets the font family, overriding the theme.
    /// </summary>
    public string FontFamily { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the text is bold.
    /// </summary>
    public bool? Bold { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the text is italic.
    /// </summary>
    public bool? Italic { get; set; }

    /// <summary>
    /// Gets or sets the text colour.
    /// </summary>
    public string Color { get; set; }

    /// <summary>
    /// Gets or sets the fill colour behind the block.
    /// </summary>
    public string BackgroundColor { get; set; }

    /// <summary>
    /// Gets or sets the space above the block, in points.
    /// </summary>
    public double? SpaceBefore { get; set; }

    /// <summary>
    /// Gets or sets the space below the block, in points.
    /// </summary>
    public double? SpaceAfter { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the block is kept on the same page as the next one.
    /// </summary>
    public bool? KeepWithNext { get; set; }
}
