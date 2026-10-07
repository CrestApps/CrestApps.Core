namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// An annotation a <c>manage_pdf_annotations</c> call adds, or the changes it makes to one.
/// </summary>
internal sealed class PdfAnnotationSpec
{
    /// <summary>
    /// Gets or sets the id of the annotation an update changes.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Gets or sets the kind: <c>note</c>, <c>highlight</c>, <c>underline</c>, <c>strikeout</c>,
    /// <c>squiggly</c>, <c>rectangle</c>, <c>ellipse</c>, <c>line</c>, <c>free_text</c> or <c>stamp</c>.
    /// </summary>
    public string Type { get; set; }

    /// <summary>
    /// Gets or sets the one-based page.
    /// </summary>
    public int? Page { get; set; }

    /// <summary>
    /// Gets or sets the pages a text search looks on.
    /// </summary>
    public string Pages { get; set; }

    /// <summary>
    /// Gets or sets the text a highlight, underline, strikeout or squiggly marks.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Gets or sets which occurrence of <see cref="Text"/> is marked, one-based; every one when unset.
    /// </summary>
    public int? Occurrence { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the text search matches case.
    /// </summary>
    public bool? MatchCase { get; set; }

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
    /// Gets or sets where a line ends, in points from the left edge.
    /// </summary>
    public double? X2 { get; set; }

    /// <summary>
    /// Gets or sets where a line ends, in points from the top edge.
    /// </summary>
    public double? Y2 { get; set; }

    /// <summary>
    /// Gets or sets the comment: a note's text, a text box's text or a stamp's label.
    /// </summary>
    public string Contents { get; set; }

    /// <summary>
    /// Gets or sets the author.
    /// </summary>
    public string Author { get; set; }

    /// <summary>
    /// Gets or sets the subject.
    /// </summary>
    public string Subject { get; set; }

    /// <summary>
    /// Gets or sets the colour: the mark, the outline or the text.
    /// </summary>
    public string Color { get; set; }

    /// <summary>
    /// Gets or sets the colour a rectangle, ellipse or text box is filled with.
    /// </summary>
    public string FillColor { get; set; }

    /// <summary>
    /// Gets or sets the opacity, from 0 to 1.
    /// </summary>
    public double? Opacity { get; set; }

    /// <summary>
    /// Gets or sets the outline width, in points.
    /// </summary>
    public double? BorderWidth { get; set; }

    /// <summary>
    /// Gets or sets the font size of a text box.
    /// </summary>
    public double? FontSize { get; set; }

    /// <summary>
    /// Gets or sets a note's icon: <c>Comment</c>, <c>Note</c>, <c>Help</c>, <c>Key</c>, <c>Insert</c>,
    /// <c>Paragraph</c> or <c>NewParagraph</c>.
    /// </summary>
    public string Icon { get; set; }
}
