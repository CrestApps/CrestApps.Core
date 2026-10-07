namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// An element to insert on a slide.
/// </summary>
public sealed class PresentationElementSpec
{
    /// <summary>
    /// Gets or sets what kind of element to insert.
    /// </summary>
    public PresentationElementSpecKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the name shown in the selection pane. A name is generated when none is given.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets where the element goes. An element with no position is given a sensible default for its
    /// kind: tables, charts and pictures fill the content area, text boxes and shapes are centred.
    /// </summary>
    public PresentationBoundsSpec Bounds { get; set; }

    /// <summary>
    /// Gets or sets the clockwise rotation in degrees.
    /// </summary>
    public double? Rotation { get; set; }

    /// <summary>
    /// Gets or sets whether the element is mirrored left to right.
    /// </summary>
    public bool? FlipHorizontal { get; set; }

    /// <summary>
    /// Gets or sets whether the element is mirrored top to bottom.
    /// </summary>
    public bool? FlipVertical { get; set; }

    /// <summary>
    /// Gets or sets the text of a text box or shape.
    /// </summary>
    public IList<PresentationParagraphSpec> Paragraphs { get; set; } = [];

    /// <summary>
    /// Gets or sets how the element's text looks, laid over the deck's recorded text style.
    /// </summary>
    public PresentationTextStyle TextStyle { get; set; }

    /// <summary>
    /// Gets or sets how the element is painted and outlined, laid over the deck's recorded shape style.
    /// </summary>
    public PresentationShapeStyle ShapeStyle { get; set; }

    /// <summary>
    /// Gets or sets the outline of a shape, as a friendly name (<c>rounded_rectangle</c>) or a preset name
    /// (<c>roundRect</c>).
    /// </summary>
    public string Geometry { get; set; }

    /// <summary>
    /// Gets or sets the icon to draw, for an icon.
    /// </summary>
    public string Icon { get; set; }

    /// <summary>
    /// Gets or sets the start of a line, as a distance from the left edge of the slide.
    /// </summary>
    public PresentationLength? LineStartX { get; set; }

    /// <summary>
    /// Gets or sets the start of a line, as a distance from the top edge of the slide.
    /// </summary>
    public PresentationLength? LineStartY { get; set; }

    /// <summary>
    /// Gets or sets the end of a line, as a distance from the left edge of the slide.
    /// </summary>
    public PresentationLength? LineEndX { get; set; }

    /// <summary>
    /// Gets or sets the end of a line, as a distance from the top edge of the slide.
    /// </summary>
    public PresentationLength? LineEndY { get; set; }

    /// <summary>
    /// Gets or sets the element a line starts from, so it runs edge to edge between two elements.
    /// </summary>
    public string ConnectFrom { get; set; }

    /// <summary>
    /// Gets or sets the element a line runs to.
    /// </summary>
    public string ConnectTo { get; set; }

    /// <summary>
    /// Gets or sets the arrowhead at the start of a line.
    /// </summary>
    public string StartArrow { get; set; }

    /// <summary>
    /// Gets or sets the arrowhead at the end of a line.
    /// </summary>
    public string EndArrow { get; set; }

    /// <summary>
    /// Gets or sets the table, for a table.
    /// </summary>
    public PresentationTableSpec Table { get; set; }

    /// <summary>
    /// Gets or sets the chart, for a chart.
    /// </summary>
    public PresentationChartSpec Chart { get; set; }

    /// <summary>
    /// Gets or sets the picture, for a picture.
    /// </summary>
    public PresentationImageData Image { get; set; }

    /// <summary>
    /// Gets or sets how a picture fits its box: <c>contain</c> keeps the whole picture, <c>cover</c> fills
    /// the box and crops the overflow, <c>stretch</c> fills the box and distorts.
    /// </summary>
    public string ImageFit { get; set; }

    /// <summary>
    /// Gets or sets the address of a video or audio clip.
    /// </summary>
    public string MediaUrl { get; set; }

    /// <summary>
    /// Gets or sets the alternative text a screen reader announces.
    /// </summary>
    public string AltText { get; set; }

    /// <summary>
    /// Gets or sets whether the element is decorative and needs no alternative text.
    /// </summary>
    public bool? Decorative { get; set; }

    /// <summary>
    /// Gets or sets where clicking the element goes.
    /// </summary>
    public PresentationLinkSpec Link { get; set; }

    /// <summary>
    /// Gets or sets the elements of a group.
    /// </summary>
    public IList<PresentationElementSpec> Children { get; set; } = [];

    /// <summary>
    /// Gets or sets the diagram, for a diagram element.
    /// </summary>
    public PresentationDiagramSpec Diagram { get; set; }
}
