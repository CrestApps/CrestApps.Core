namespace CrestApps.Core.AI.Documents.Presentations.Models;

/// <summary>
/// Where clicking an element or a piece of text goes.
/// </summary>
public sealed class PresentationHyperlink
{
    /// <summary>
    /// Gets or sets the external address, when the link leaves the deck.
    /// </summary>
    public string Url { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the slide the link jumps to, when it stays in the deck.
    /// </summary>
    public uint? TargetSlideId { get; set; }

    /// <summary>
    /// Gets or sets the number of the slide the link jumps to, resolved from <see cref="TargetSlideId"/>, or
    /// <see langword="null"/> when that slide no longer exists.
    /// </summary>
    public int? TargetSlideNumber { get; set; }

    /// <summary>
    /// Gets or sets a relative jump that names no slide: <c>next</c>, <c>previous</c>, <c>first</c>,
    /// <c>last</c> or <c>end</c>.
    /// </summary>
    public string Action { get; set; }

    /// <summary>
    /// Gets or sets the tooltip shown when the pointer rests on the link.
    /// </summary>
    public string Tooltip { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the link points at something the package no longer holds: a
    /// relationship that is missing, or a slide that was deleted.
    /// </summary>
    public bool IsBroken { get; set; }

    /// <summary>
    /// Describes the destination in a few words for a tool response.
    /// </summary>
    /// <returns>The description.</returns>
    public string Describe()
    {
        if (!string.IsNullOrEmpty(Url))
        {
            return Url;
        }

        if (TargetSlideNumber is not null)
        {
            return $"slide {TargetSlideNumber}";
        }

        if (!string.IsNullOrEmpty(Action))
        {
            return Action + " slide";
        }

        return IsBroken ? "a missing target" : "no target";
    }
}
