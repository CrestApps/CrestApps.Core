namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// Where a link should go: an address, a slide in the deck, or a relative jump.
/// </summary>
public sealed class PresentationLinkSpec
{
    /// <summary>
    /// Gets or sets an external address (<c>https:</c> or <c>mailto:</c>).
    /// </summary>
    public string Url { get; set; }

    /// <summary>
    /// Gets or sets the number of the slide to jump to, resolved against the deck as it stands when the link
    /// is written.
    /// </summary>
    public int? SlideNumber { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the slide to jump to, which survives slides moving around it.
    /// </summary>
    public uint? SlideId { get; set; }

    /// <summary>
    /// Gets or sets a relative jump: <c>next</c>, <c>previous</c>, <c>first</c>, <c>last</c> or <c>end</c>.
    /// </summary>
    public string Action { get; set; }

    /// <summary>
    /// Gets or sets the tooltip.
    /// </summary>
    public string Tooltip { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an existing link should be removed.
    /// </summary>
    public bool Remove { get; set; }
}
