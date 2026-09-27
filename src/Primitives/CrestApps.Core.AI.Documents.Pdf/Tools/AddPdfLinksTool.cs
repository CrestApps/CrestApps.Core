using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Adds web links and links to pages, over an area of a page or over text found on it.
/// </summary>
internal sealed class AddPdfLinksTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.AddPdfLinks;

    private const int MaxLinks = 500;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Password}},
            {{PdfToolSchemas.SaveAs}},
            "links": {
              "type": "array",
              "description": "The links. Each is placed over 'text' (every match, or only 'occurrence') or over the area x, y, width, height on 'page', and opens 'url' or goes to 'target_page'.",
              "items": {
                "type": "object",
                "properties": {
                  "page": { "type": "integer", "description": "One-based page. With 'text' it limits the search to that page; otherwise every page is searched." },
                  "x": { "type": "number", "description": "Left edge, in points from the left of the page." },
                  "y": { "type": "number", "description": "Top edge, in points from the top of the page." },
                  "width": { "type": "number" },
                  "height": { "type": "number" },
                  "text": { "type": "string", "description": "Text to place the link over, instead of an area." },
                  "occurrence": { "type": "integer", "description": "One-based: link only this match of 'text', counting in page order." },
                  "url": { "type": "string", "description": "An http, https or mailto address." },
                  "target_page": { "type": "integer", "description": "One-based page of this document to go to." }
                }
              }
            }
          },
          "required": ["links"],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddPdfLinksTool"/> class.
    /// </summary>
    public AddPdfLinksTool()
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
    public override string Description => "Adds clickable links to a PDF: web links (http, https or mailto) or links to a page of the same document, placed over text found on the pages or over an area given in points from the page's top-left corner. Saves a working copy.";

    /// <summary>
    /// Adds the links.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var requests = arguments.Get<List<PdfLinkRequest>>("links");

        if (requests is null || requests.Count == 0)
        {
            throw new PdfToolException("Pass 'links': for example [{\"text\": \"Contact us\", \"url\": \"mailto:help@example.com\"}] or [{\"page\": 1, \"x\": 72, \"y\": 700, \"width\": 120, \"height\": 14, \"target_page\": 3}].");
        }

        var password = arguments.GetString("password");

        return await context.MutateAsync(async state =>
        {
            var target = context.FindPdf(state, arguments.Pdf());
            var bytes = await context.ReadPdfAsync(target, cancellationToken);

            using var pig = PdfFiles.OpenForReading(bytes, password);
            using var document = PdfFiles.OpenForEditing(bytes, password);

            var placed = new List<string>();
            var total = 0;

            for (var index = 0; index < requests.Count; index++)
            {
                var request = requests[index] ?? throw new PdfToolException($"Link {index + 1} is empty.");
                var label = string.Create(CultureInfo.InvariantCulture, $"Link {index + 1}");
                var (url, targetPage) = ReadDestination(request, label, pig.NumberOfPages);
                var areas = FindAreas(request, label, pig);

                total += areas.Count;

                if (total > MaxLinks)
                {
                    throw new PdfToolException($"That places more than {MaxLinks:N0} links; add them in smaller batches or use 'occurrence'.");
                }

                foreach (var (page, box, description) in areas)
                {
                    var pdfPage = document.Pages[page - 1];
                    var rectangle = new PdfRectangle(new XPoint(box.Left, box.Bottom), new XPoint(box.Right, box.Top));

                    if (url is not null)
                    {
                        pdfPage.AddWebLink(rectangle, url);
                    }
                    else
                    {
                        pdfPage.AddDocumentLink(rectangle, targetPage);
                    }

                    placed.Add(string.Create(CultureInfo.InvariantCulture, $"- page {page}, {description} → {url ?? $"page {targetPage}"}"));
                }
            }

            var signatures = PdfObjects.DescribeBrokenSignatures(document);
            var sync = PdfMetadataSync.Capture(document);
            var saved = sync.Save(document, context.TimeProvider.GetUtcNow());
            var working = await context.SaveWorkingFileAsync(state, target, arguments.GetString("save_as"), saved, $"Added {placed.Count} links", cancellationToken);
            var answer = new StringBuilder();

            answer.AppendLine(PdfPropertiesToolText.Saved(target, working));
            answer.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Added {placed.Count} link(s):"));

            foreach (var line in placed.Take(50))
            {
                answer.AppendLine(line);
            }

            if (placed.Count > 50)
            {
                answer.AppendLine(string.Create(CultureInfo.InvariantCulture, $"(and {placed.Count - 50} more)"));
            }

            PdfPropertiesToolText.AppendNotes(answer, signatures, PdfProtection.DescribeDropped(bytes, password));

            return answer.ToString().TrimEnd();
        }, cancellationToken);
    }

    private static (string Url, int TargetPage) ReadDestination(PdfLinkRequest request, string label, int pageCount)
    {
        var hasUrl = !string.IsNullOrWhiteSpace(request.Url);

        if (hasUrl == request.TargetPage.HasValue)
        {
            throw new PdfToolException($"{label} needs exactly one destination: 'url' or 'target_page'.");
        }

        if (request.TargetPage is { } targetPage)
        {
            if (targetPage < 1 || targetPage > pageCount)
            {
                throw new PdfToolException($"{label} goes to page {targetPage}, but the document has {pageCount} page(s).");
            }

            return (null, targetPage);
        }

        var url = request.Url.Trim();

        if (url.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }
        else if (!url.Contains(':', StringComparison.Ordinal) && url.Contains('@', StringComparison.Ordinal))
        {
            url = "mailto:" + url;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https" or "mailto") ||
            url.Any(char.IsControl))
        {
            throw new PdfToolException($"{label}: only http, https and mailto addresses can be linked, and \"{request.Url}\" is not one.");
        }

        return (uri.Scheme == "mailto" ? url : uri.AbsoluteUri, 0);
    }

    private static List<(int Page, PdfBox Box, string Description)> FindAreas(PdfLinkRequest request, string label, PigDocument pig)
    {
        var areas = new List<(int Page, PdfBox Box, string Description)>();

        if (request.Page is { } requested && (requested < 1 || requested > pig.NumberOfPages))
        {
            throw new PdfToolException($"{label} is on page {requested}, but the document has {pig.NumberOfPages} page(s).");
        }

        if (!string.IsNullOrWhiteSpace(request.Text))
        {
            List<int> pages = request.Page is { } only
                ? [only]
                : [.. Enumerable.Range(1, pig.NumberOfPages)];

            var matches = new List<PdfTextMatch>();

            foreach (var number in pages)
            {
                matches.AddRange(PdfTextFinder.Find(pig.GetPage(number), request.Text, isRegex: false, matchCase: false, wholeWord: false, MaxLinks));

                if (matches.Count > MaxLinks)
                {
                    break;
                }
            }

            if (matches.Count == 0)
            {
                throw new PdfToolException($"{label}: \"{request.Text}\" was not found{(request.Page is { } page ? $" on page {page}" : string.Empty)}. Check the wording with search_pdf, or give an area.");
            }

            if (request.Occurrence is { } occurrence)
            {
                if (occurrence < 1 || occurrence > matches.Count)
                {
                    throw new PdfToolException($"{label}: \"{request.Text}\" appears {matches.Count} time(s); occurrence {occurrence} does not exist.");
                }

                matches = [matches[occurrence - 1]];
            }

            foreach (var match in matches)
            {
                foreach (var box in match.Boxes)
                {
                    areas.Add((match.PageNumber, box.Inflate(1), $"\"{match.Text}\""));
                }
            }

            return areas;
        }

        if (request.Page is not { } pageNumber)
        {
            throw new PdfToolException($"{label} needs 'text', or a 'page' with x, y, width and height.");
        }

        if (request.X is not { } x || request.Y is not { } y || request.Width is not { } width || request.Height is not { } height)
        {
            throw new PdfToolException($"{label} needs x, y, width and height (points from the page's top-left corner), or 'text'.");
        }

        if (width <= 0 || height <= 0)
        {
            throw new PdfToolException($"{label} needs a positive width and height.");
        }

        var pigPage = pig.GetPage(pageNumber);
        var visible = PdfBox.VisibleArea(pigPage);
        var area = PdfBox.FromTopLeft(pigPage, x, y, width, height);
        var clipped = new PdfBox(
            Math.Max(area.Left, visible.Left),
            Math.Max(area.Bottom, visible.Bottom),
            Math.Min(area.Right, visible.Right),
            Math.Min(area.Top, visible.Top));

        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            throw new PdfToolException(string.Create(CultureInfo.InvariantCulture, $"{label} is outside page {pageNumber}, which is {visible.Width:0} by {visible.Height:0} points."));
        }

        areas.Add((pageNumber, clipped, clipped.Describe(pigPage)));

        return areas;
    }
}
