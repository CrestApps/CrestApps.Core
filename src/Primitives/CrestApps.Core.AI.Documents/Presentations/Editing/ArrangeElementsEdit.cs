namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// Lines up, spaces out, restacks or lays out elements on a slide.
/// </summary>
public sealed class ArrangeElementsEdit : PresentationEdit
{
    /// <summary>
    /// The arrangements the engine understands.
    /// </summary>
    public static readonly IReadOnlyList<string> Actions =
    [
        "align_left",
        "align_center",
        "align_right",
        "align_top",
        "align_middle",
        "align_bottom",
        "distribute_horizontally",
        "distribute_vertically",
        "match_width",
        "match_height",
        "match_size",
        "bring_forward",
        "send_backward",
        "bring_to_front",
        "send_to_back",
        "snap_to_grid",
        "auto_layout",
        "fit_to_slide",
        "center_on_slide",
    ];

    /// <summary>
    /// Gets or sets the number of the slide.
    /// </summary>
    public int Slide { get; set; }

    /// <summary>
    /// Gets or sets the elements to arrange. Layout actions use every element that is not a title, footer,
    /// date or slide number when the list is empty.
    /// </summary>
    public IList<string> Elements { get; set; } = [];

    /// <summary>
    /// Gets or sets the arrangement, one of <see cref="Actions"/>.
    /// </summary>
    public string Action { get; set; }

    /// <summary>
    /// Gets or sets what alignment is measured against: <c>selection</c> (the elements' own extent),
    /// <c>slide</c>, or <c>content</c> (the slide's content area).
    /// </summary>
    public string RelativeTo { get; set; } = "selection";

    /// <summary>
    /// Gets or sets the grid pitch for <c>snap_to_grid</c>, or the gap between elements for
    /// <c>auto_layout</c>.
    /// </summary>
    public PresentationLength? Spacing { get; set; }

    /// <summary>
    /// Gets or sets the number of columns <c>auto_layout</c> arranges the elements in. A balanced number is
    /// chosen when it is not set.
    /// </summary>
    public int? Columns { get; set; }
}
