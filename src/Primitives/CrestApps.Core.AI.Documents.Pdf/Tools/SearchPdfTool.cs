using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Finds text or a pattern in a PDF and reports every match with its page, the text around it and where it
/// is drawn, so the model can cite a page and later highlight, link or redact exactly that text.
/// </summary>
internal sealed class SearchPdfTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.SearchPdf;

    private const int DefaultMaxResults = 50;
    private const int MaxMaxResults = 500;
    private const int MaxMatchesCountedPerPage = 5_000;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Pages}},
            {{PdfToolSchemas.Password}},
            "query": {
              "type": "string",
              "description": "The text to find, or a .NET regular expression when 'regex' is true. Plain text matches across line breaks and ignores extra spaces."
            },
            "regex": {
              "type": "boolean",
              "description": "Whether 'query' is a regular expression. Defaults to false."
            },
            "match_case": {
              "type": "boolean",
              "description": "Whether upper and lower case must match. Defaults to false."
            },
            "whole_word": {
              "type": "boolean",
              "description": "Whether a match must be a whole word (not part of a longer word). Defaults to false."
            },
            "max_results": {
              "type": "integer",
              "description": "The most matches listed in detail, 1-500. Defaults to 50; every match is still counted."
            }
          },
          "required": ["query"],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchPdfTool"/> class.
    /// </summary>
    public SearchPdfTool()
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
    public override string Description => "Finds text or a regular expression in a PDF and returns the total, a count per page and each match with its page, the matched text, a snippet of the text around it and its box (x, y, w, h in points from the page's top-left corner). Use it to locate where something is said, to cite pages, or before highlighting, linking or redacting text.";

    /// <summary>
    /// Searches the PDF.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The conversation's PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matches.</returns>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var query = arguments.GetString("query")
            ?? throw new PdfToolException("Pass 'query', the text or pattern to find.");

        var isRegex = arguments.GetBoolean("regex") ?? false;
        var matchCase = arguments.GetBoolean("match_case") ?? false;
        var wholeWord = arguments.GetBoolean("whole_word") ?? false;
        var maxResults = Math.Clamp(arguments.GetInt("max_results") ?? DefaultMaxResults, 1, MaxMaxResults);

        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var bytes = await context.ReadPdfAsync(source, cancellationToken);
        using var pdf = PdfFiles.OpenForReading(bytes, arguments.GetString("password"));
        var pages = PdfPageRange.Parse(arguments.GetPages(), pdf.NumberOfPages);

        var perPage = new List<(int Page, int Count)>();
        var details = new List<string>();
        var total = 0;
        var textless = new List<int>();

        foreach (var number in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = pdf.GetPage(number);

            if (page.Letters.Count == 0)
            {
                textless.Add(number);

                continue;
            }

            var matches = PdfTextFinder.Find(page, query, isRegex, matchCase, wholeWord, MaxMatchesCountedPerPage);

            if (matches.Count == 0)
            {
                continue;
            }

            perPage.Add((number, matches.Count));
            total += matches.Count;

            foreach (var match in matches)
            {
                if (details.Count >= maxResults)
                {
                    break;
                }

                var boxes = string.Join("; ", match.Boxes.Select(box => box.Describe(page)));

                details.Add(FormattableString.Invariant($"{details.Count + 1}. p{number} \"{PdfTextPatterns.Truncate(match.Text, 120)}\" — {match.Snippet} [{boxes}]"));
            }
        }

        var writer = new PdfResponseWriter(context.Options.MaxToolResponseCharacters);
        var searched = PdfPageSelection.Describe(pages, pdf.NumberOfPages);

        if (total == 0)
        {
            writer.Line($"No matches for \"{query}\" in \"{source.Name}\" ({searched} searched).");

            if (!isRegex && !wholeWord && !matchCase)
            {
                writer.Line("Try a shorter or different wording, or a regular expression with regex: true.");
            }
        }
        else
        {
            writer.Line($"Found {total} match(es) for \"{query}\" in \"{source.Name}\" ({searched} searched). Boxes are x, y, w, h in points from the page's top-left corner.");
            writer.Line("Matches per page: " + string.Join(", ", perPage.Take(200).Select(entry => FormattableString.Invariant($"p{entry.Page}: {entry.Count}"))) + (perPage.Count > 200 ? ", …" : string.Empty));
            writer.Line();

            var shown = 0;

            foreach (var detail in details)
            {
                if (!writer.TryLine(detail))
                {
                    break;
                }

                shown++;
            }

            if (shown < total)
            {
                writer.Line();
                writer.Line(shown < details.Count
                    ? $"[Listed {shown} of {total} matches to stay within the answer size. Narrow 'pages' to see the others.]"
                    : $"[Listed the first {shown} of {total} matches. Raise max_results (up to {MaxMaxResults}) or narrow 'pages' to see the others.]");
            }
        }

        if (textless.Count > 0)
        {
            writer.Line();
            writer.Line($"Pages {PdfPageRange.Describe(textless)} have no text layer and were not searched; they may be scanned — ocr_pdf can read them.");
        }

        return writer.ToString();
    }
}
