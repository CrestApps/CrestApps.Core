using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Returns a PDF's logical structure — headings with their levels, paragraphs, list items, tables and
/// figures with their captions — in reading order, with page numbers.
/// </summary>
/// <remarks>
/// Headings are told by type size compared with the body text across the pages read, tables and drawings
/// come from the ingestion reader, and running heads, page numbers and the text inside tables and charts
/// are left out of the flow, as a reader leaves them out.
/// </remarks>
internal sealed class ExtractPdfStructureTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.ExtractPdfStructure;

    private const int DefaultParagraphCharacters = 300;
    private const int MaxParagraphCharacters = 5_000;
    private const int DefaultMaxPages = 100;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Pages}},
            {{PdfToolSchemas.Password}},
            "format": {
              "type": "string",
              "enum": ["outline", "markdown", "json"],
              "description": "How the structure is returned: 'outline' (default) one line per element indented under its heading; 'markdown' as a Markdown document; 'json' as a list of elements."
            },
            "max_paragraph_characters": {
              "type": "integer",
              "description": "The most characters of each paragraph shown. Defaults to 300; 0 shows paragraphs in full."
            }
          },
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtractPdfStructureTool"/> class.
    /// </summary>
    public ExtractPdfStructureTool()
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
    public override string Description => "Returns a PDF's logical structure in reading order with page numbers: headings (with level), paragraphs (shortened), list items, tables (size and header row) and figures (with captions), leaving out running heads and page numbers. Use it to understand how a document is organised, to find a section, or before converting or summarising it.";

    /// <summary>
    /// Reads the structure.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The conversation's PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The structure.</returns>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var format = (arguments.GetString("format") ?? "outline").Trim().ToLowerInvariant();

        if (format is not ("outline" or "markdown" or "json"))
        {
            throw new PdfToolException($"\"{format}\" is not a structure format. Use 'outline', 'markdown' or 'json'.");
        }

        var maxParagraph = Math.Clamp(arguments.GetInt("max_paragraph_characters") ?? DefaultParagraphCharacters, 0, MaxParagraphCharacters);
        var password = arguments.GetString("password");

        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var bytes = await context.ReadPdfAsync(source, cancellationToken);
        using var pdf = PdfFiles.OpenForReading(bytes, password);
        var selection = arguments.GetPages();
        var pages = PdfPageRange.Parse(selection, pdf.NumberOfPages);
        var limited = selection is null && pages.Count > DefaultMaxPages;

        // A whole long book is more than one answer shows; it is read in parts the model asks for.
        if (limited)
        {
            pages = pages.Take(DefaultMaxPages).ToList();
        }

        var readable = PdfReadableCopy.WithoutPassword(bytes, password, pdf.IsEncrypted);
        var content = await PdfIngestedContent.ReadAsync(arguments.Services, readable, source.Name, pages, pdf.NumberOfPages, cancellationToken);
        var layout = PdfLayoutAnalyzer.Analyze(pdf, pages, content, encodeImages: false, cancellationToken);
        var elements = PdfStructureBuilder.Build(layout);

        var writer = new PdfResponseWriter(context.Options.MaxToolResponseCharacters);
        var scope = PdfPageSelection.Describe(pages, pdf.NumberOfPages);

        writer.Line($"Structure of \"{source.Name}\" ({scope}): {Summarize(elements, layout)}.");

        if (elements.Count == 0)
        {
            writer.Line("No text was found on these pages. They may be scanned images; ocr_pdf can read them.");

            return writer.ToString();
        }

        writer.Line();

        var stoppedAtPage = format switch
        {
            "markdown" => WriteLines(writer, PdfStructureWriter.ToMarkdown(elements, pageMarkers: true, maxParagraph, maxTableRows: 20).Split('\n'), elements),
            "json" => WriteJson(writer, PdfStructureWriter.ToJsonModel(elements, maxParagraph, includeTableRows: false), elements),
            _ => WriteLines(writer, PdfStructureWriter.ToOutline(elements, maxParagraph), elements),
        };

        if (stoppedAtPage > 0)
        {
            var remaining = pages.Where(page => page >= stoppedAtPage).ToList();

            writer.Line();
            writer.Line($"[Stopped at page {stoppedAtPage} to stay within the answer size. Ask for pages \"{PdfPageRange.Describe(remaining)}\" to continue, or lower max_paragraph_characters.]");
        }
        else if (limited)
        {
            writer.Line();
            writer.Line(FormattableString.Invariant($"[Only the first {DefaultMaxPages} of {pdf.NumberOfPages} pages were read. Ask for pages \"{DefaultMaxPages + 1}-\" to continue.]"));
        }

        if (content.Warning is not null)
        {
            writer.Line(content.Warning);
        }

        return writer.ToString();
    }

    private static string Summarize(List<PdfStructureElement> elements, PdfDocumentLayout layout)
    {
        var counts = elements.GroupBy(element => element.Type).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        int CountOf(string type)
        {
            return counts.GetValueOrDefault(type);
        }

        var running = layout.Pages.Sum(page => page.Blocks.Count(block => block.Role is PdfLayoutRoles.Header or PdfLayoutRoles.Footer));
        var text = FormattableString.Invariant($"{CountOf(PdfStructureElement.HeadingType)} heading(s), {CountOf(PdfStructureElement.ParagraphType)} paragraph(s), {CountOf(PdfStructureElement.ListItemType)} list item(s), {CountOf(PdfStructureElement.TableType)} table(s), {CountOf(PdfStructureElement.FigureType)} figure(s)");

        if (layout.BodyFontSize > 0)
        {
            text += FormattableString.Invariant($"; body text {layout.BodyFontSize}pt");
        }

        if (running > 0)
        {
            text += FormattableString.Invariant($"; {running} running head/foot or page-number block(s) left out");
        }

        return text;
    }

    private static int WriteLines(PdfResponseWriter writer, IReadOnlyList<string> lines, List<PdfStructureElement> elements)
    {
        // Lines and elements do not correspond one to one in Markdown, so the page to resume from is found
        // from the page markers and outline prefixes the lines carry.
        var page = elements[0].Page;

        foreach (var line in lines)
        {
            page = PageOf(line) ?? page;

            if (!writer.TryLine(line))
            {
                return page;
            }
        }

        return 0;
    }

    private static int? PageOf(string line)
    {
        const string Marker = "<!-- page ";

        if (line.StartsWith(Marker, StringComparison.Ordinal))
        {
            var end = line.IndexOf(' ', Marker.Length);

            return end > Marker.Length && int.TryParse(line.AsSpan(Marker.Length, end - Marker.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? number
                : null;
        }

        if (line.Length > 1 && line[0] == 'p' && char.IsAsciiDigit(line[1]))
        {
            var end = line.IndexOf(' ', StringComparison.Ordinal);

            return end > 1 && int.TryParse(line.AsSpan(1, end - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? number
                : null;
        }

        return null;
    }

    private static int WriteJson(PdfResponseWriter writer, List<object> model, List<PdfStructureElement> elements)
    {
        var builder = new StringBuilder("[");
        var stoppedAt = 0;

        for (var index = 0; index < model.Count; index++)
        {
            var item = PdfReadingJson.Serialize(model[index]);

            if (!writer.Fits(builder.Length + item.Length + 2))
            {
                stoppedAt = elements[index].Page;

                break;
            }

            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(item);
        }

        writer.TryLine(builder.Append(']').ToString());

        return stoppedAt;
    }
}
