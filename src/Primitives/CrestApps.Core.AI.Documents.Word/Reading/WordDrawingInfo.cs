using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Reading;

/// <summary>
/// One drawing in a document — a picture, a chart, a shape, a group or a SmartArt graphic — and how it is placed.
/// </summary>
internal sealed class WordDrawingInfo
{
    /// <summary>
    /// Gets or sets the drawing's id, unique in the document, that tools name it by.
    /// </summary>
    public uint Id { get; set; }

    /// <summary>
    /// Gets or sets what the drawing is.
    /// </summary>
    public WordDrawingKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the drawing's name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the alternative text a screen reader reads.
    /// </summary>
    public string AltText { get; set; }

    /// <summary>
    /// Gets or sets the drawing's title.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the width in points.
    /// </summary>
    public double Width { get; set; }

    /// <summary>
    /// Gets or sets the height in points.
    /// </summary>
    public double Height { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the drawing floats rather than sitting in a line of text.
    /// </summary>
    public bool IsFloating { get; set; }

    /// <summary>
    /// Gets or sets how text wraps around a floating drawing.
    /// </summary>
    public string Wrap { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a floating drawing sits behind the text.
    /// </summary>
    public bool BehindText { get; set; }

    /// <summary>
    /// Gets or sets the horizontal offset of a floating drawing, in points, from <see cref="HorizontalFrom"/>.
    /// </summary>
    public double OffsetX { get; set; }

    /// <summary>
    /// Gets or sets the vertical offset of a floating drawing, in points, from <see cref="VerticalFrom"/>.
    /// </summary>
    public double OffsetY { get; set; }

    /// <summary>
    /// Gets or sets what a floating drawing's horizontal position is measured from, such as <c>page</c>,
    /// <c>margin</c> or <c>column</c>.
    /// </summary>
    public string HorizontalFrom { get; set; }

    /// <summary>
    /// Gets or sets what a floating drawing's vertical position is measured from, such as <c>page</c>,
    /// <c>margin</c> or <c>paragraph</c>.
    /// </summary>
    public string VerticalFrom { get; set; }

    /// <summary>
    /// Gets or sets a floating drawing's horizontal alignment, such as <c>center</c>, when it is aligned rather
    /// than offset.
    /// </summary>
    public string HorizontalAlignment { get; set; }

    /// <summary>
    /// Gets or sets a floating drawing's vertical alignment when it is aligned rather than offset.
    /// </summary>
    public string VerticalAlignment { get; set; }

    /// <summary>
    /// Gets or sets the relationship id of the picture or chart the drawing shows.
    /// </summary>
    public string RelationshipId { get; set; }

    /// <summary>
    /// Gets or sets the text a shape or text box holds.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Gets or sets the drawing element.
    /// </summary>
    public Drawing Element { get; set; }
}

/// <summary>
/// What a drawing is.
/// </summary>
internal enum WordDrawingKind
{
    /// <summary>
    /// A picture.
    /// </summary>
    Picture,

    /// <summary>
    /// A chart.
    /// </summary>
    Chart,

    /// <summary>
    /// A shape or a text box.
    /// </summary>
    Shape,

    /// <summary>
    /// A group of shapes and pictures.
    /// </summary>
    Group,

    /// <summary>
    /// A drawing canvas.
    /// </summary>
    Canvas,

    /// <summary>
    /// A SmartArt graphic.
    /// </summary>
    SmartArt,

    /// <summary>
    /// Anything else.
    /// </summary>
    Other,
}
