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
