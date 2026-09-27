namespace CrestApps.Core.AI.Documents.Presentations.Models;

/// <summary>
/// One element on a slide — a shape, a picture, a table, a chart, a group — with everything it inherits from
/// its layout, master and theme already resolved, so the reader describes what the slide actually shows.
/// </summary>
public sealed class PresentationElement
{
    /// <summary>
    /// Gets or sets the element's identifier, unique within its slide. Tools address elements by it.
    /// </summary>
    public uint Id { get; set; }

    /// <summary>
    /// Gets or sets the element's name as the selection pane shows it.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets what kind of element this is.
    /// </summary>
    public PresentationElementKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the placeholder type the element fills, such as <c>title</c>, <c>ctrTitle</c>,
    /// <c>subTitle</c>, <c>body</c>, <c>dt</c>, <c>ftr</c> or <c>sldNum</c>, when it is a placeholder.
    /// </summary>
    public string PlaceholderType { get; set; }

    /// <summary>
    /// Gets or sets the placeholder index that ties the element to its layout, when it is a placeholder.
    /// </summary>
    public uint? PlaceholderIndex { get; set; }

    /// <summary>
    /// Gets or sets where the element sits on the slide, with the transforms of any enclosing groups applied.
    /// </summary>
    public PresentationBounds Bounds { get; set; }

    /// <summary>
    /// Gets or sets the clockwise rotation in degrees.
    /// </summary>
    public double Rotation { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the element is mirrored left to right.
    /// </summary>
    public bool FlipHorizontal { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the element is mirrored top to bottom.
    /// </summary>
    public bool FlipVertical { get; set; }

    /// <summary>
    /// Gets or sets the preset outline of a shape, such as <c>rect</c>, <c>roundRect</c> or <c>ellipse</c>,
    /// or <c>custom</c> for a freeform.
    /// </summary>
    public string Geometry { get; set; }

    /// <summary>
    /// Gets or sets the adjustment values of the preset outline, keyed by name, such as the corner radius of
    /// a rounded rectangle.
    /// </summary>
    public IDictionary<string, long> GeometryAdjustments { get; set; } = new Dictionary<string, long>(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the outline of a freeform shape, in the shape's own box, when <see cref="Geometry"/> is
    /// <c>custom</c>.
    /// </summary>
    public IList<PresentationPath> Paths { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the element casts a shadow.
    /// </summary>
    public bool HasShadow { get; set; }

    /// <summary>
    /// Gets or sets how the element is painted.
    /// </summary>
    public PresentationFill Fill { get; set; } = PresentationFill.None;

    /// <summary>
    /// Gets or sets the element's outline.
    /// </summary>
    public PresentationLine Line { get; set; } = new();

    /// <summary>
    /// Gets or sets the element's text, when it holds any.
    /// </summary>
    public PresentationTextBody Text { get; set; }

    /// <summary>
    /// Gets or sets the table, when the element is one.
    /// </summary>
    public PresentationTable Table { get; set; }

    /// <summary>
    /// Gets or sets the chart, when the element is one.
    /// </summary>
    public PresentationChart Chart { get; set; }

    /// <summary>
    /// Gets or sets the picture, when the element is one or a video or audio clip shows one as its poster.
    /// </summary>
    public PresentationImage Image { get; set; }

    /// <summary>
    /// Gets or sets the elements a group holds, in drawing order.
    /// </summary>
    public IList<PresentationElement> Children { get; set; } = [];

    /// <summary>
    /// Gets or sets the alternative text a screen reader announces.
    /// </summary>
    public string AltText { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the author marked the element as decorative, so it needs no
    /// alternative text.
    /// </summary>
    public bool IsDecorative { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the element is hidden.
    /// </summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// Gets or sets where clicking the element goes.
    /// </summary>
    public PresentationHyperlink Link { get; set; }

    /// <summary>
    /// Gets or sets the address of linked media, for a video or audio element.
    /// </summary>
    public string MediaUrl { get; set; }

    /// <summary>
    /// Gets or sets a short description of content the reader cannot render, such as <c>SmartArt graphic</c>,
    /// so a preview can label its place rather than leave it blank.
    /// </summary>
    public string UnsupportedDescription { get; set; }

    /// <summary>
    /// Gets or sets the text of a SmartArt graphic or other content read for its words only.
    /// </summary>
    public string FallbackText { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the element comes from the slide's layout or master rather
    /// than the slide itself, such as a logo every slide shows.
    /// </summary>
    public bool IsInherited { get; set; }

    /// <summary>
    /// Gets or sets the position of the element in the slide's drawing order, counting from 1 at the back.
    /// </summary>
    public int ZOrder { get; set; }

    /// <summary>
    /// Gets a value indicating whether the element is a title placeholder.
    /// </summary>
    public bool IsTitle => PlaceholderType is "title" or "ctrTitle";

    /// <summary>
    /// Gets a short name for the kind of element, as tools report it.
    /// </summary>
    public string KindName => Kind switch
    {
        PresentationElementKind.TextBox => "text_box",
        PresentationElementKind.EmbeddedObject => "embedded_object",
        PresentationElementKind.Model3D => "3d_model",
        _ => Kind.ToString().ToLowerInvariant(),
    };

    /// <summary>
    /// Walks the element and, for a group, everything inside it.
    /// </summary>
    /// <returns>The element followed by its descendants.</returns>
    public IEnumerable<PresentationElement> DescendantsAndSelf()
    {
        yield return this;

        foreach (var child in Children)
        {
            foreach (var descendant in child.DescendantsAndSelf())
            {
                yield return descendant;
            }
        }
    }
}
