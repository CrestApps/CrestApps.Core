namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// Where an element should go: explicit coordinates, a named placement, or both.
/// </summary>
/// <remarks>
/// A model reasons about "the left half" far better than about 457,200 EMUs, so a placement names an area of
/// the slide's content region and the engine works out the rectangle. Any coordinate given alongside a
/// placement overrides that part of it, so <c>right_half</c> with a height of 200 points is the right half,
/// 200 points tall.
/// </remarks>
public sealed class PresentationBoundsSpec
{
    /// <summary>
    /// The placements the engine understands.
    /// </summary>
    public static readonly IReadOnlyList<string> Placements =
    [
        "content",
        "full",
        "slide",
        "left_half",
        "right_half",
        "top_half",
        "bottom_half",
        "left_third",
        "center_third",
        "right_third",
        "left_two_thirds",
        "right_two_thirds",
        "top_left",
        "top_right",
        "bottom_left",
        "bottom_right",
        "center",
        "title",
        "footer",
    ];

    /// <summary>
    /// Gets or sets the distance from the left edge of the slide.
    /// </summary>
    public PresentationLength? X { get; set; }

    /// <summary>
    /// Gets or sets the distance from the top edge of the slide.
    /// </summary>
    public PresentationLength? Y { get; set; }

    /// <summary>
    /// Gets or sets the width.
    /// </summary>
    public PresentationLength? Width { get; set; }

    /// <summary>
    /// Gets or sets the height.
    /// </summary>
    public PresentationLength? Height { get; set; }

    /// <summary>
    /// Gets or sets a named area of the slide, one of <see cref="Placements"/>.
    /// </summary>
    public string Placement { get; set; }

    /// <summary>
    /// Gets a value indicating whether the specification says anything about position or size.
    /// </summary>
    public bool IsEmpty => X is null && Y is null && Width is null && Height is null && string.IsNullOrEmpty(Placement);
}
