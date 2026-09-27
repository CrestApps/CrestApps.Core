using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Permanently removes text, images and areas from a PDF.
/// </summary>
internal sealed class RedactPdfTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.RedactPdf;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Pages}},
            {{PdfToolSchemas.Password}},
            {{PdfToolSchemas.SaveAs}},
            "texts": { "type": "array", "items": { "type": "string" }, "description": "Exact phrases to remove wherever they appear." },
            "patterns": { "type": "array", "items": { "type": "string" }, "description": "Regular expressions whose matches are removed." },
            "categories": {
              "type": "array",
              "items": { "type": "string", "enum": ["email", "phone", "url", "ip_address", "credit_card", "iban", "us_ssn", "date", "money", "percentage", "us_zip_code", "all_sensitive"] },
              "description": "Kinds of value to find and remove (as find_pdf_sensitive_data reports them). 'all_sensitive' means email, phone, card numbers, IBANs, SSNs and IP addresses."
            },
            "areas": {
              "type": "array",
              "description": "Areas to remove, in points from the page's top-left corner.",
              "items": {
                "type": "object",
                "properties": {
                  "page": { "type": "integer" },
                  "x": { "type": "number" },
                  "y": { "type": "number" },
                  "width": { "type": "number" },
                  "height": { "type": "number" }
                },
                "required": ["page", "x", "y", "width", "height"]
              }
            },
            "whole_pages": { "type": "string", "description": "Pages to blank out entirely, e.g. '3,5'." },
            "match_case": { "type": "boolean" },
            "fill_color": { "type": "string", "description": "Colour of the boxes over removed content. Defaults to black." },
            "overlay_text": { "type": "string", "description": "Text printed in each box, e.g. 'REDACTED'." },
            "keep_annotations": { "type": "boolean", "description": "Keep comments, links and form fields that sit over removed content. Defaults to false: they are removed too." }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedactPdfTool"/> class.
    /// </summary>
    public RedactPdfTool()
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
    public override string Description => "Permanently redacts a PDF: removes the text and images under the given phrases, regex matches, sensitive-data categories (emails, phone numbers, card numbers, IBANs, SSNs…), explicit areas or whole pages from the page content itself — not just covering them — removes comments and fields over them, draws boxes over the areas, and verifies that nothing is left to extract. Saves a working PDF; the upload is never changed. Use find_pdf_sensitive_data first to review what will be removed.";

    /// <summary>
    /// Redacts the PDF.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var categories = arguments.GetStrings("categories", splitCommas: true)
            .SelectMany(category => category.Equals("all_sensitive", StringComparison.OrdinalIgnoreCase) ? PdfPatternLibrary.SensitiveKinds : [category.Trim().ToLowerInvariant()])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var unknown = categories.Where(category => !PdfPatternLibrary.AllKinds.Contains(category, StringComparer.OrdinalIgnoreCase)).ToList();

        if (unknown.Count > 0)
        {
            throw new PdfToolException($"Unknown categories: {string.Join(", ", unknown)}. Use: {string.Join(", ", PdfPatternLibrary.AllKinds)}, all_sensitive.");
        }

        var request = new PdfRedactionRequest
        {
            Texts = arguments.GetStrings("texts"),
            Patterns = arguments.GetStrings("patterns"),
            Categories = categories,
            MatchCase = arguments.GetBoolean("match_case") == true,
            Fill = PdfColor.Parse(arguments.GetString("fill_color"), new PdfColor(0, 0, 0)),
            OverlayText = arguments.GetString("overlay_text"),
            RemoveAnnotations = arguments.GetBoolean("keep_annotations") != true,
        };

        var areaSpecs = arguments.Get<List<AreaDefinition>>("areas") ?? [];

        return await context.MutateAsync(async state =>
        {
            var target = context.FindPdf(state, arguments.Pdf());
            var bytes = await context.ReadPdfAsync(target, cancellationToken);
            var password = arguments.GetString("password");

            using (var pdf = PdfFiles.OpenForReading(bytes, password))
            {
                if (arguments.GetPages() is { } pages)
                {
                    request.Pages = [.. PdfPageRange.Parse(pages, pdf.NumberOfPages)];
                }

                if (arguments.GetString("whole_pages") is { } whole)
                {
                    request.WholePages = PdfPageRange.Parse(whole, pdf.NumberOfPages);
                }

                foreach (var area in areaSpecs)
                {
                    if (area.Page < 1 || area.Page > pdf.NumberOfPages)
                    {
                        throw new PdfToolException($"Page {area.Page} does not exist; the document has {pdf.NumberOfPages} page(s).");
                    }

                    request.Areas.Add((area.Page, PdfBox.FromTopLeft(pdf.GetPage(area.Page), area.X, area.Y, area.Width, area.Height)));
                }
            }

            if (request.Texts.Count + request.Patterns.Count + request.Categories.Count + request.Areas.Count + request.WholePages.Count == 0)
            {
                throw new PdfToolException("Say what to redact: 'texts', 'patterns', 'categories', 'areas' or 'whole_pages'.");
            }

            var (areas, matches) = PdfRedactor.FindAreas(bytes, password, request);

            if (areas.Count == 0)
            {
                return $"Nothing matching the request was found in {target.Describe()}; nothing was redacted.";
            }

            var (redacted, results) = PdfRedactor.Apply(bytes, password, areas, request);
            var totalAreas = results.Sum(result => result.Areas);
            var working = await context.SaveWorkingFileAsync(
                state,
                target,
                arguments.GetString("save_as"),
                redacted,
                $"Redacted {totalAreas} area(s) on page(s) {PdfPageRange.Describe(results.Select(result => result.Page))}",
                cancellationToken);

            var response = new StringBuilder();

            response.Append("Redacted ").Append(totalAreas.ToString(CultureInfo.InvariantCulture)).Append(" area(s) and saved working PDF \"").Append(working.Name).AppendLine("\":");

            foreach (var result in results)
            {
                response.Append("- page ").Append(result.Page).Append(": ").Append(result.Areas).Append(" area(s), ")
                    .Append(result.Glyphs).Append(" character(s) and ").Append(result.Images).Append(" image(s) removed from the content");

                if (result.Annotations > 0)
                {
                    response.Append(", ").Append(result.Annotations).Append(" annotation(s)/field(s) removed");
                }

                if (!string.IsNullOrEmpty(result.Error))
                {
                    response.Append(" — WARNING: ").Append(result.Error).Append("; the area is covered but its content may remain");
                }
                else if (result.Remaining > 0)
                {
                    response.Append(" — WARNING: ").Append(result.Remaining).Append(" character(s) are still extractable inside the area (drawn in a way this tool cannot rewrite); tell the user the redaction is incomplete there");
                }

                response.AppendLine(".");
            }

            var categoriesFound = matches.Where(match => match.Kind is not null).GroupBy(match => match.Kind).Select(group => $"{group.Count()} {group.Key}").ToList();

            if (categoriesFound.Count > 0)
            {
                response.Append("Categories removed: ").Append(string.Join(", ", categoriesFound)).AppendLine(".");
            }

            var incomplete = results.Any(result => result.Remaining > 0 || !string.IsNullOrEmpty(result.Error));

            response.Append(incomplete
                ? "Some content could not be removed; do not describe the redaction as complete."
                : "Verified: none of the removed text can be extracted from the result.");

            response.Append(" Document metadata, bookmarks and attachments are not changed by redaction; use sanitize_pdf to clear those.");
            response.Append(target.IsUpload ? " The uploaded file was not changed." : string.Empty);

            return response.ToString();
        }, cancellationToken);
    }

    private sealed class AreaDefinition
    {
        public int Page { get; set; }

        public double X { get; set; }

        public double Y { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }
    }
}
