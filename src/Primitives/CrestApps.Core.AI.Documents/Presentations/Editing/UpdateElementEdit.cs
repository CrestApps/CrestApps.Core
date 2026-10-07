namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// Changes an existing element: its text, position, size, rotation, style, picture, alternative text or
/// link. Properties left unset are not touched.
/// </summary>
public sealed class UpdateElementEdit : PresentationEdit
{
    /// <summary>
    /// Gets or sets the number of the slide the element is on.
    /// </summary>
    public int Slide { get; set; }

    /// <summary>
    /// Gets or sets the element, by its identifier, its name, or a placeholder role such as <c>title</c>,
    /// <c>subtitle</c> or <c>body</c>.
    /// </summary>
    public string Element { get; set; }

    /// <summary>
    /// Gets or sets new text for the element. The text is written into the element's existing paragraphs so
    /// their formatting is kept: each new paragraph takes the formatting of the paragraph it replaces.
    /// </summary>
    public IList<PresentationParagraphSpec> Paragraphs { get; set; }

    /// <summary>
    /// Gets or sets the paragraph, counting from 1, that <see cref="Paragraphs"/> replaces. When set, only
    /// that paragraph changes and the rest of the text is left alone.
    /// </summary>
    public int? ParagraphNumber { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="Paragraphs"/> are added after the existing text
    /// rather than replacing it.
    /// </summary>
    public bool AppendParagraphs { get; set; }

    /// <summary>
    /// Gets or sets a new position or size.
    /// </summary>
    public PresentationBoundsSpec Bounds { get; set; }

    /// <summary>
    /// Gets or sets a factor to scale the element by, about its centre, such as 1.5 or 0.5.
    /// </summary>
    public double? Scale { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a new width or height keeps the element's proportions, the
    /// missing side following from the one given.
    /// </summary>
    public bool KeepAspectRatio { get; set; }

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
    /// Gets or sets how the element's text looks.
    /// </summary>
    public PresentationTextStyle TextStyle { get; set; }

    /// <summary>
    /// Gets or sets how the element is painted and outlined.
    /// </summary>
    public PresentationShapeStyle ShapeStyle { get; set; }

    /// <summary>
    /// Gets or sets a new outline for a shape.
    /// </summary>
    public string Geometry { get; set; }

    /// <summary>
    /// Gets or sets the alternative text. An empty string removes it.
    /// </summary>
    public string AltText { get; set; }

    /// <summary>
    /// Gets or sets whether the element is decorative.
    /// </summary>
    public bool? Decorative { get; set; }

    /// <summary>
    /// Gets or sets the element's name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets where clicking the element goes.
    /// </summary>
    public PresentationLinkSpec Link { get; set; }

    /// <summary>
    /// Gets or sets how a picture is cropped.
    /// </summary>
    public PresentationCropSpec Crop { get; set; }

    /// <summary>
    /// Gets or sets a picture that replaces the element's picture in the same frame.
    /// </summary>
    public PresentationImageData ReplacementImage { get; set; }

    /// <summary>
    /// Gets or sets whether the element is hidden.
    /// </summary>
    public bool? Hidden { get; set; }
}
