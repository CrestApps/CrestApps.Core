using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// What one data source search found: the text handed to the model, plus the figures and tables among the
/// hits as objects a caller can act on rather than parse back out of prose.
/// </summary>
/// <remarks>
/// A chat turn only needs the text. A client that can show a picture — an MCP host, a custom UI — needs to
/// know which hits were pictures and where to fetch them, which is what the typed lists are for.
/// </remarks>
public sealed class DataSourceRetrievalResult : IAIToolContentProvider
{
    /// <summary>
    /// Gets the rendered search results, exactly as they are handed to the model.
    /// </summary>
    public string Text { get; init; }

    /// <summary>
    /// Gets the figures and charts among the hits.
    /// </summary>
    public IReadOnlyList<RetrievedFigure> Figures { get; init; } = [];

    /// <summary>
    /// Gets the tables among the hits.
    /// </summary>
    public IReadOnlyList<RetrievedTable> Tables { get; init; } = [];

    /// <summary>
    /// Returns the rendered search results, so a caller that expected a string keeps working unchanged.
    /// </summary>
    /// <returns>The rendered search results.</returns>
    public override string ToString()
    {
        return Text;
    }

    /// <summary>
    /// Renders the result as its text plus a link to every figure among the hits.
    /// </summary>
    /// <returns>The contents.</returns>
    /// <remarks>
    /// The links are what turn "there is a chart on page 8" into something a client can actually open. The
    /// text is unchanged, so a transport that only understands prose loses nothing.
    /// </remarks>
    public IReadOnlyList<AIContent> ToContents()
    {
        var contents = new List<AIContent>
        {
            new TextContent(Text),
        };

        foreach (var figure in Figures)
        {
            if (string.IsNullOrWhiteSpace(figure.Uri) || !Uri.TryCreate(figure.Uri, UriKind.Absolute, out var uri))
            {
                continue;
            }

            var link = new UriContent(uri, string.IsNullOrWhiteSpace(figure.MediaType) ? "image/png" : figure.MediaType)
            {
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["name"] = figure.Caption ?? figure.Title ?? figure.Id,
                },
            };

            contents.Add(link);
        }

        return contents;
    }
}

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

/// <summary>
/// One table among the search hits.
/// </summary>
public sealed class RetrievedTable
{
    /// <summary>
    /// Gets the canonical identifier of the table.
    /// </summary>
    public string Id { get; init; }

    /// <summary>
    /// Gets the citation label the table is rendered under, for example <c>[tbl:1]</c>.
    /// </summary>
    public string Label { get; init; }

    /// <summary>
    /// Gets the table's title, which is its caption when it has one.
    /// </summary>
    public string Title { get; init; }

    /// <summary>
    /// Gets the caption printed with the table, when it has one.
    /// </summary>
    public string Caption { get; init; }

    /// <summary>
    /// Gets the table's column headings, when they are known.
    /// </summary>
    public string Columns { get; init; }

    /// <summary>
    /// Gets the page the table was printed on, when it is known.
    /// </summary>
    public int? Page { get; init; }
}
