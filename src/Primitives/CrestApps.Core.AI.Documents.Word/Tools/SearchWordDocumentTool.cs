using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Reading;
using CrestApps.Core.AI.Documents.Word.Rendering;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.Word.Tools;

/// <summary>
/// Finds text in a document and reports where it is: the element, its page and the words around it.
/// </summary>
internal sealed class SearchWordDocumentTool : WordToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = WordToolNames.SearchWordDocument;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{WordToolSchemas.Document}},
            "query": { "type": "string", "description": "The words to find." },
            "regex": { "type": "boolean", "description": "Treat 'query' as a regular expression." },
            "match_case": { "type": "boolean" },
            "whole_word": { "type": "boolean" },
            "include_headers": { "type": "boolean", "description": "Also search headers, footers, footnotes and comments." },
            "page_numbers": { "type": "boolean", "description": "Report the page of each match (lays the document out). Default true." },
            "limit": { "type": "integer", "description": "Most matches to return. Default 50." }
          },
          "required": ["query"],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchWordDocumentTool"/> class.
    /// </summary>
    public SearchWordDocumentTool()
        : base(Schema)
    {
    }

    /// <summary>
    /// Gets the name.
    /// </summary>
    public override string Name => TheName;

    /// <summary>
    /// Gets the description.
    /// </summary>
    public override string Description => "Searches a Word document for words or a 'regex' pattern and returns every match with the [id] of its element, its page, the nearest heading and the words around it, so you can cite it, edit it with update_word_content or comment on it. Optionally also searches headers, footers, footnotes and comments.";

    /// <summary>
    /// Searches the document.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The Word context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(WordToolArguments arguments, WordToolContext context, CancellationToken cancellationToken)
    {
        var query = arguments.GetRawString("query");

        if (string.IsNullOrEmpty(query))
        {
            throw new WordToolException("Pass the 'query' to find.");
        }

        var pattern = arguments.GetBoolean("regex") == true ? query : Regex.Escape(query);

        if (arguments.GetBoolean("whole_word") == true)
        {
            pattern = $@"\b(?:{pattern})\b";
        }

        Regex regex;

        try
        {
            regex = new Regex(pattern, (arguments.GetBoolean("match_case") == true ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException exception)
        {
            throw new WordToolException($"'query' is not a valid regular expression: {exception.Message}");
        }

        var source = await context.FindDocumentAsync(arguments.Document(), cancellationToken);

        using var package = await context.OpenAsync(source, cancellationToken);

        _ = package.Ids;

        var limit = Math.Clamp(arguments.GetInt("limit") ?? 50, 1, 500);
        var layout = arguments.GetBoolean("page_numbers") ?? true ? WordPreview.Layout(package, context.Services) : null;
        var styles = new WordStyleIndex(package.MainPart);
        var answer = new StringBuilder();
        var total = 0;
        string heading = null;

        foreach (var paragraph in package.Body.Descendants<Paragraph>())
        {
            if (styles.OutlineLevelOf(paragraph) is not null)
            {
                heading = WordText.Of(paragraph);
            }

            total += Report(answer, regex, paragraph, total, limit, (_, match) =>
            {
                var where = new StringBuilder();

                where.Append('[').Append(WordParagraphIds.Of(paragraph)).Append(']');

                if (paragraph.Ancestors<Table>().LastOrDefault() is { } table)
                {
                    where.Append(" in table [").Append(WordParagraphIds.Of(table)).Append(']');
                }

                if (layout is not null && PageOfMatch(layout, paragraph, match.Index) is > 0 and var page)
                {
                    var number = page <= layout.Pages.Count ? layout.Pages[page - 1].DisplayNumber : null;

                    where.Append(", page ").Append(string.IsNullOrEmpty(number) ? page.ToString(CultureInfo.InvariantCulture) : number);
                }

                if (heading is not null)
                {
                    where.Append(", under \"").Append(WordText.Clip(heading, 60)).Append('"');
                }

                return where.ToString();
            });
        }

        if (arguments.GetBoolean("include_headers") == true)
        {
            var main = package.MainPart;
            var others = main.HeaderParts.Select(part => ("header", (DocumentFormat.OpenXml.OpenXmlElement)part.Header))
                .Concat(main.FooterParts.Select(part => ("footer", (DocumentFormat.OpenXml.OpenXmlElement)part.Footer)))
                .Append(("footnote", main.FootnotesPart?.Footnotes))
                .Append(("endnote", main.EndnotesPart?.Endnotes))
                .Append(("comment", main.WordprocessingCommentsPart?.Comments))
                .Where(item => item.Item2 is not null);

            foreach (var (kind, root) in others)
            {
                foreach (var paragraph in root.Descendants<Paragraph>())
                {
                    total += Report(answer, regex, paragraph, total, limit, (_, _) => "in a " + kind);
                }
            }
        }

        if (total == 0)
        {
            return $"\"{query}\" was not found in {source.Describe()}.";
        }

        var header = total > limit ? $"{total} match(es) of \"{query}\" in {source.Describe()}; the first {limit}:" : $"{total} match(es) of \"{query}\" in {source.Describe()}:";

        return header + "\n" + answer.ToString().TrimEnd();
    }

    // The page the matched words are on. A paragraph that runs over a page break is followed through the text the
    // layout drew for it, counting the characters before the match; spaces are not counted, since lines drop
    // them where they break. A list number or text a tracked change deleted, which the layout also draws, can
    // shift the count by a few characters, so a match right at a page break may be given the other page.
    private static int PageOfMatch(WordLayout layout, Paragraph paragraph, int index)
    {
        var first = 0;
        var last = 0;

        for (DocumentFormat.OpenXml.OpenXmlElement current = paragraph; current is not null && first == 0; current = current.Parent)
        {
            if (layout.FirstPage.TryGetValue(current, out first))
            {
                last = layout.LastPage.TryGetValue(current, out var end) ? end : first;
            }
        }

        if (first <= 0 || last <= first)
        {
            return first;
        }

        var before = WordText.Of(paragraph).Take(index).Count(character => !char.IsWhiteSpace(character));
        var seen = 0;

        for (var page = first; page <= last && page <= layout.Pages.Count; page++)
        {
            foreach (var item in layout.Pages[page - 1].Items.OfType<WordTextItem>().Where(item => ReferenceEquals(item.Source, paragraph) && item.MarkupColor != "C00000"))
            {
                seen += item.Text.Count(character => !char.IsWhiteSpace(character));

                if (seen > before)
                {
                    return page;
                }
            }
        }

        return first;
    }

    private static int Report(StringBuilder answer, Regex regex, Paragraph paragraph, int found, int limit, Func<Paragraph, Match, string> describe)
    {
        var text = WordText.Of(paragraph);

        if (text.Length == 0)
        {
            return 0;
        }

        var matches = regex.Matches(text);
        var shown = found;

        foreach (Match match in matches)
        {
            if (shown++ >= limit)
            {
                break;
            }

            var start = Math.Max(0, match.Index - 60);
            var end = Math.Min(text.Length, match.Index + match.Length + 60);

            answer.Append("- ").Append(describe(paragraph, match)).Append(": ")
                .Append(start > 0 ? "…" : string.Empty)
                .Append(text, start, match.Index - start)
                .Append("**").Append(match.Value).Append("**")
                .Append(text, match.Index + match.Length, end - match.Index - match.Length)
                .AppendLine(end < text.Length ? "…" : string.Empty);
        }

        return matches.Count;
    }
}
