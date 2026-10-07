using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Presentations;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Finds text across a deck's slides, tables, charts, alternative text and notes.
/// </summary>
internal sealed class SearchPresentationTool : PresentationToolBase
{
    /// <summary>
    /// The tool's name.
    /// </summary>
    public const string TheName = PresentationToolNames.SearchPresentation;

    private const int MaxMatches = 60;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchPresentationTool"/> class.
    /// </summary>
    public SearchPresentationTool()
        : base(
            TheName,
            "Searches the presentation's slide text, table cells, chart labels, alternative text and speaker notes for words or a phrase, and returns each match with its slide, element #id and surrounding text. Pass presentation \"all\" to search every presentation in the conversation.",
            PresentationSchemas.Expand("""
            {
              "type": "object",
              "properties": {
                PRESENTATION,
                "query": { "type": "string", "description": "The text to find. Several words match text containing all of them." },
                "match_case": { "type": "boolean" },
                "include_notes": { "type": "boolean", "description": "Search speaker notes too. Defaults to true." }
              },
              "required": ["query"],
              "additionalProperties": false
            }
            """))
    {
    }

    /// <summary>
    /// Searches.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PresentationToolCall call, CancellationToken cancellationToken)
    {
        var query = call.Arguments.String("query", "text", "find")?.Trim();

        if (string.IsNullOrEmpty(query))
        {
            throw new PresentationArgumentException("Give the text to search for.");
        }

        var comparison = call.Arguments.Bool("match_case") == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var reference = call.Arguments.String("presentation", "deck");
        var decks = string.Equals(reference, "all", StringComparison.OrdinalIgnoreCase)
            ? call.Session.Workspace.State.Decks.ToList()
            : [call.Deck()];

        var builder = new StringBuilder();
        var total = 0;

        foreach (var deck in decks)
        {
            var model = await call.ReadAsync(deck, cancellationToken);
            var matches = PresentationTextEntry.Collect(model, [], call.Arguments.Bool("include_notes") != false)
                .Where(entry => entry.Text.Contains(query, comparison) || words.All(word => entry.Text.Contains(word, comparison)))
                .ToList();

            if (matches.Count == 0)
            {
                continue;
            }

            builder.Append('"').Append(deck.Name).Append("\" (").Append(deck.Id).Append("): ").AppendLine(PresentationDescriber.Count(matches.Count, "match", "matches"));

            foreach (var match in matches.Take(MaxMatches - total))
            {
                builder.Append("- slide ").Append(match.Slide.ToString(CultureInfo.InvariantCulture));

                if (match.ElementId > 0)
                {
                    builder.Append(" #").Append(match.ElementId.ToString(CultureInfo.InvariantCulture));
                }

                builder.Append(' ').Append(match.Where).Append(": \"").Append(Snippet(match.Text, query, comparison)).AppendLine("\"");
            }

            total += matches.Count;

            if (total >= MaxMatches)
            {
                builder.AppendLine("(more matches not shown; search for a longer phrase)");
                break;
            }
        }

        return total == 0 ? $"\"{query}\" was not found." : builder.ToString().TrimEnd();
    }

    private static string Snippet(string text, string query, StringComparison comparison)
    {
        var flat = text.Replace('\n', ' ');
        var index = flat.IndexOf(query, comparison);

        if (flat.Length <= 160 || index < 0)
        {
            return PresentationDescriber.Excerpt(flat, 160);
        }

        var start = Math.Max(0, index - 60);
        var length = Math.Min(flat.Length - start, 160);

        return string.Concat(start > 0 ? "…" : string.Empty, flat.AsSpan(start, length), start + length < flat.Length ? "…" : string.Empty);
    }
}
