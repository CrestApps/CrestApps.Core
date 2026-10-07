namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// A diagram to lay out on a slide.
/// </summary>
public sealed class PresentationDiagramSpec
{
    /// <summary>
    /// Gets or sets the kind: <c>process</c>, <c>chevron</c>, <c>cycle</c>, <c>timeline</c>, <c>hierarchy</c>,
    /// <c>pyramid</c>, <c>funnel</c>, <c>matrix</c>, <c>venn</c> or <c>cards</c>.
    /// </summary>
    public string Kind { get; set; }

    /// <summary>
    /// Gets or sets the steps, stages or nodes, in order.
    /// </summary>
    public IList<PresentationDiagramItem> Items { get; set; } = [];

    /// <summary>
    /// Gets or sets, for a cycle or Venn diagram, text for the middle.
    /// </summary>
    public string CenterText { get; set; }

    /// <summary>
    /// Gets or sets the fill colours used in turn; the theme's accents when empty.
    /// </summary>
    public IList<string> Colors { get; set; } = [];
}
