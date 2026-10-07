using System.Globalization;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Checks how a PDF will look and behave: rendering problems, content running off the page, fonts, links and
/// layout — and, for a composed document, whether the rendered file matches its definition.
/// </summary>
internal sealed class CheckPdfQualityTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.CheckPdfQuality;

    private const string Rendering = "rendering";
    private const string Overflow = "overflow";
    private const string Fonts = "fonts";
    private const string Links = "links";
    private const string Layout = "layout";

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Pages}},
            {{PdfToolSchemas.Password}},
            "checks": {
              "type": "array",
              "items": {
                "type": "string",
                "enum": ["rendering", "overflow", "fonts", "links", "layout"]
              },
              "description": "Which checks to run. rendering: blank pages, text under 5 pt, invisible text, near-white text, overlapping lines, text cut by the page edge. overflow: content outside the page or closer to an edge than min_margin_mm. fonts: embedding, Unicode mapping, Type 3 fonts, with a font list. links: web addresses (syntax only, never visited), internal link and bookmark targets, zero-size links. layout: mixed page sizes, inconsistent margins and, for a document built with create_pdf, whether the rendered file matches its definition (page setup, running heads, page numbers, cover, contents, headings, pictures, charts, text width). Omit to run all."
            },
            "min_margin_mm": {
              "type": "number",
              "description": "The smallest distance from a page edge content should keep, in millimetres, for the overflow check. Defaults to 5."
            }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    private static readonly string[] _allChecks = [Rendering, Overflow, Fonts, Links, Layout];

    /// <summary>
    /// Initializes a new instance of the <see cref="CheckPdfQualityTool"/> class.
    /// </summary>
    public CheckPdfQualityTool()
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
    public override string Description => "Checks the visual and technical quality of a PDF before it is shared: blank pages, tiny, invisible, near-white or overlapping text, text cut by the page edge, content outside the page or too close to an edge, missing or unembedded fonts, fonts whose text extracts as garbage, broken or unsafe links and bookmarks, mixed page sizes and uneven margins. For a document built with create_pdf it also compares the rendered file with its definition: page setup, running heads, page numbers, cover, table of contents, headings, pictures, charts and tables within the text width. Use it after building or editing a PDF, or when asked whether a PDF looks right. Returns a checklist with page numbers and positions. Links are checked for syntax and internal targets only; no web address is visited.";

    /// <summary>
    /// Checks the PDF.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The report.</returns>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var requested = ReadChecks(arguments.GetStrings("checks"));
        var minMargin = arguments.GetDouble("min_margin_mm") ?? 5;

        if (minMargin is < 0 or > 100)
        {
            throw new PdfToolException("'min_margin_mm' must be between 0 and 100 millimetres.");
        }

        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var bytes = await context.ReadPdfAsync(source, cancellationToken);

        using var document = PdfInspectedDocument.Open(bytes, arguments.GetString("password"), requireContent: true, requireObjects: false);

        var selection = arguments.GetPages();
        var pages = PdfPageRange.Parse(selection, document.PageCount);
        var checks = new PdfCheckList();
        var inspections = new Dictionary<int, PdfPageInspection>();

        List<PdfPageInspection> Inspect(IEnumerable<int> numbers)
        {
            var result = new List<PdfPageInspection>();

            foreach (var number in numbers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!inspections.TryGetValue(number, out var inspection))
                {
                    inspection = PdfPageInspection.Inspect(document.Content, number);
                    inspections[number] = inspection;
                }

                if (inspection is not null)
                {
                    result.Add(inspection);
                }
            }

            return result;
        }

        if (requested.Contains(Rendering))
        {
            checks.Category = "Rendering";
            PdfRenderingChecks.Run(checks, Inspect(pages));
        }

        if (requested.Contains(Overflow))
        {
            checks.Category = "Content overflow";
            PdfOverflowChecks.Run(checks, Inspect(pages), PdfPageSizes.FromMillimetres(minMargin));
        }

        if (requested.Contains(Fonts))
        {
            checks.Category = "Fonts";

            if (document.Objects is null)
            {
                checks.Info("Fonts", "Not checked: the file's objects could not be read (" + document.ObjectsError + "). Run validate_pdf.");
            }
            else
            {
                PdfFontChecks.Run(checks, document.Objects, pages, cancellationToken);
            }
        }

        if (requested.Contains(Links))
        {
            checks.Category = "Links";

            if (document.Objects is null)
            {
                checks.Info("Links", "Not checked: the file's objects could not be read (" + document.ObjectsError + "). Run validate_pdf.");
            }
            else
            {
                PdfLinkChecks.Run(checks, document.Objects, document.Content, pages);
            }
        }

        if (requested.Contains(Layout))
        {
            checks.Category = "Layout";

            var definition = source.IsComposed
                ? source.Working.Definition
                : null;

            var options = context.Services.GetService<IOptions<PdfCompositionOptions>>()?.Value ?? new PdfCompositionOptions();
            var selected = Inspect(pages);
            var everyPage = definition is null
                ? selected
                : Inspect(Enumerable.Range(1, document.PageCount));

            PdfLayoutChecks.Run(checks, selected, definition is not null && PdfDefinitionChecks.SizesMatch(definition, everyPage, options));

            if (definition is not null)
            {
                var rendered = await context.RenderAsync(source.Working, cancellationToken);

                checks.Category = "Layout compared with the document's definition";
                PdfDefinitionChecks.Run(checks, definition, everyPage, options, rendered.Warnings);
            }
        }

        var failures = checks.Count(PdfCheckStatus.Fail);
        var warnings = checks.Count(PdfCheckStatus.Warn);
        var verdict = failures > 0
            ? "problems that need fixing were found"
            : warnings > 0
                ? "no errors, with points to review"
                : "no quality problems found";

        var scope = string.IsNullOrWhiteSpace(selection) || pages.Count == document.PageCount
            ? string.Create(CultureInfo.InvariantCulture, $"all {document.PageCount} page(s)")
            : string.Create(CultureInfo.InvariantCulture, $"{PdfCheckList.Pages(pages)} of {document.PageCount}");

        var notes = new List<string>
        {
            "The checks read the geometry the file records (text, pictures, paths); they cannot judge what a picture shows. Look at pages with preview_pdf to confirm a finding.",
        };

        if (requested.Contains(Links))
        {
            notes.Add("Links were checked for syntax and internal targets only; no web address was visited.");
        }

        return checks.Render(
            $"Quality of {source.Describe()}, {scope}; checks: {string.Join(", ", _allChecks.Where(requested.Contains))}.",
            verdict,
            bySeverity: false,
            notes);
    }

    private static HashSet<string> ReadChecks(List<string> values)
    {
        var checks = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in values)
        {
            var normalized = value.Trim().ToLowerInvariant();

            var check = normalized switch
            {
                "all" => null,
                _ when normalized.Contains("render", StringComparison.Ordinal) => Rendering,
                _ when normalized.Contains("overflow", StringComparison.Ordinal) || normalized.Contains("margin", StringComparison.Ordinal) => Overflow,
                _ when normalized.Contains("font", StringComparison.Ordinal) => Fonts,
                _ when normalized.Contains("link", StringComparison.Ordinal) => Links,
                _ when normalized.Contains("layout", StringComparison.Ordinal) => Layout,
                _ => throw new PdfToolException($"\"{value}\" is not a check this tool runs. Use any of: {string.Join(", ", _allChecks)}."),
            };

            if (check is null)
            {
                checks.UnionWith(_allChecks);
            }
            else
            {
                checks.Add(check);
            }
        }

        if (checks.Count == 0)
        {
            checks.UnionWith(_allChecks);
        }

        return checks;
    }
}
