namespace CrestApps.Core.AI.Services;

/// <summary>
/// One figure or chart among the search hits.
/// </summary>
public sealed class RetrievedFigure
{
    /// <summary>
    /// Gets the canonical identifier of the figure.
    /// </summary>
    public string Id { get; init; }

    /// <summary>
    /// Gets the citation label the figure is rendered under, for example <c>[fig:1]</c>.
    /// </summary>
    public string Label { get; init; }

    /// <summary>
    /// Gets the figure's title, which is its caption when it has one.
    /// </summary>
    public string Title { get; init; }

    /// <summary>
    /// Gets the caption printed with the figure, when it has one.
    /// </summary>
    public string Caption { get; init; }

    /// <summary>
    /// Gets the logical address of the figure's picture, which an MCP client reads the picture through.
    /// </summary>
    public string Uri { get; init; }

    /// <summary>
    /// Gets the address a person can open, when the host exposes one. This is what a chat answer embeds to
    /// show the picture; it is <see langword="null"/> when no link resolver is registered for the row's
    /// reference type.
    /// </summary>
    public string Link { get; init; }

    /// <summary>
    /// Gets the media type of the picture, when it is known.
    /// </summary>
    public string MediaType { get; init; }

    /// <summary>
    /// Gets how far the values shown on the figure can be trusted, when it is a chart.
    /// </summary>
    public string ValueConfidence { get; init; }

    /// <summary>
    /// Gets the kind of knowledge the row holds, which is <c>figure</c> or <c>chart</c>.
    /// </summary>
    public string ContentType { get; init; }

    /// <summary>
    /// Gets the page the figure was printed on, when it is known.
    /// </summary>
    public int? Page { get; init; }
}
