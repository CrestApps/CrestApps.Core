using System.Globalization;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Describes how pages are laid out: their columns, the blocks of text in reading order with the role each
/// plays, and their figures and tables — the reading order a question such as "what comes after the chart"
/// depends on.
/// </summary>
internal sealed class AnalyzePdfLayoutTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.AnalyzePdfLayout;

    private const int DefaultPages = 5;
    private const int MaxBlockCharacters = 120;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Pages}},
            {{PdfToolSchemas.Password}}
          },
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnalyzePdfLayoutTool"/> class.
    /// </summary>
    public AnalyzePdfLayoutTool()
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
    public override string Description => "Analyses page layout: the number of text columns, every text block in reading order with its role (heading with level, paragraph, list item, caption, running header or footer, table text) and box, and the figures (pictures and vector drawings) and tables on the page, with a one-line summary per page. Use it for the reading order of a complex page or to see how a document is laid out. Without 'pages' a long document is analysed from its first 5 pages.";

    /// <summary>
    /// Analyses the layout.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The conversation's PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The layout, page by page.</returns>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var password = arguments.GetString("password");
        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var bytes = await context.ReadPdfAsync(source, cancellationToken);
        using var pdf = PdfFiles.OpenForReading(bytes, password);

        var selection = arguments.GetPages();
        var pages = PdfPageRange.Parse(selection, pdf.NumberOfPages);
        var limited = false;

        if (selection is null && pages.Count > DefaultPages)
        {
            pages = pages.Take(DefaultPages).ToList();
            limited = true;
        }

        var readable = PdfReadableCopy.WithoutPassword(bytes, password, pdf.IsEncrypted);
        var content = await PdfIngestedContent.ReadAsync(arguments.Services, readable, source.Name, pages, pdf.NumberOfPages, cancellationToken);
        var layout = PdfLayoutAnalyzer.Analyze(pdf, pages, content, encodeImages: false, cancellationToken);
        var writer = new PdfResponseWriter(context.Options.MaxToolResponseCharacters);

        writer.Line($"Layout of \"{source.Name}\", pages {PdfPageRange.Describe(pages)} of {pdf.NumberOfPages}. Boxes are x, y, w, h in points from the page's top-left corner.");
        writer.Line(DescribeTypeSizes(layout));

        if (limited)
        {
            writer.Line(FormattableString.Invariant($"Only the first {DefaultPages} pages were analysed; pass 'pages' to analyse others."));
        }

        var stoppedAt = 0;

        foreach (var page in layout.Pages)
        {
            if (!WritePage(writer, page, allowPartial: page == layout.Pages[0]))
            {
                stoppedAt = page.PageNumber;

                break;
            }
        }

        if (stoppedAt > 0)
        {
            var remaining = pages.Where(page => page >= stoppedAt).ToList();

            writer.Line();
            writer.Line($"[Stopped at page {stoppedAt} to stay within the answer size. Ask for pages \"{PdfPageRange.Describe(remaining)}\" to continue.]");
        }

        if (content.Warning is not null)
        {
            writer.Line(content.Warning);
        }

        return writer.ToString();
    }

    private static string DescribeTypeSizes(PdfDocumentLayout layout)
    {
        if (layout.BodyFontSize <= 0)
        {
            return "No text was found; the pages may be scanned images (ocr_pdf can read them).";
        }

        var text = "Body text is set at " + layout.BodyFontSize.ToString("0.#", CultureInfo.InvariantCulture) + "pt";

        if (layout.HeadingSizes.Count > 0)
        {
            text += "; headings at " + string.Join(", ", layout.HeadingSizes.Select((size, index) => FormattableString.Invariant($"{size:0.#}pt (level {index + 1})")));
        }

        return text + ".";
    }

    private static bool WritePage(PdfResponseWriter writer, PdfPageLayout page, bool allowPartial)
    {
        var turned = page.Rotation is 90 or 270;
        var width = turned
            ? page.Visible.Height
            : page.Visible.Width;
        var height = turned
            ? page.Visible.Width
            : page.Visible.Height;
        var lines = new List<string>
        {
            string.Empty,
            FormattableString.Invariant($"Page {page.PageNumber} ({PdfPageSizes.Describe(width, height)}) — {Summary(page)}"),
        };

        if (page.Columns.Count > 1)
        {
            lines.Add("Columns: " + string.Join(", ", page.Columns.Select((column, index) => FormattableString.Invariant($"{index + 1}: x {Math.Round(column.Left - page.Visible.Left)}–{Math.Round(column.Right - page.Visible.Left)}"))));
        }

        if (page.Blocks.Count > 0)
        {
            lines.Add("Reading order:");

            foreach (var block in page.Blocks)
            {
                lines.Add(FormattableString.Invariant($"{block.Order}. {DescribeRole(block, page)} [{page.Describe(block.Box)}]: \"{PdfTextPatterns.Truncate(PdfTextPatterns.OneLine(block.Text), MaxBlockCharacters)}\""));
            }
        }

        foreach (var figure in page.Figures)
        {
            var kind = figure.Kind == PdfRegion.DrawingKind
                ? "drawing (vector graphics)"
                : FormattableString.Invariant($"image {figure.PixelWidth}×{figure.PixelHeight} px");
            var role = figure.IsDecoration
                ? ", page furniture"
                : string.Empty;

            lines.Add("Figure: " + kind + role + Where(page, figure.Box) + Caption(figure));
        }

        foreach (var table in page.Tables)
        {
            lines.Add(FormattableString.Invariant($"Table: {table.Rows?.Count ?? 0} rows × {table.ColumnCount} columns") + Where(page, table.Box) + Caption(table));
        }

        // A page is written whole or not at all, so the answer does not stop half way through one — unless
        // the first page alone is more than an answer holds.
        if (!writer.Fits(lines.Sum(line => line.Length + 1)))
        {
            if (allowPartial)
            {
                foreach (var line in lines)
                {
                    if (!writer.TryLine(line))
                    {
                        break;
                    }
                }
            }

            return false;
        }

        foreach (var line in lines)
        {
            writer.TryLine(line);
        }

        return true;
    }

    private static string Where(PdfPageLayout page, PdfBox? box)
    {
        return box is { } value
            ? " [" + page.Describe(value) + "]"
            : string.Empty;
    }

    private static string Caption(PdfRegion region)
    {
        return string.IsNullOrWhiteSpace(region.Caption)
            ? string.Empty
            : " — caption: " + region.Caption;
    }

    private static string Summary(PdfPageLayout page)
    {
        var columns = Math.Max(1, page.Columns.Count);
        var figures = page.Figures.Count(figure => !figure.IsDecoration);
        var parts = new List<string>
        {
            columns == 1 ? "1 column" : FormattableString.Invariant($"{columns} columns"),
            page.Blocks.Count == 1 ? "1 block" : FormattableString.Invariant($"{page.Blocks.Count} blocks"),
            figures == 1 ? "1 figure" : FormattableString.Invariant($"{figures} figures"),
        };

        if (page.Tables.Count > 0)
        {
            parts.Add(page.Tables.Count == 1 ? "1 table" : FormattableString.Invariant($"{page.Tables.Count} tables"));
        }

        var headings = page.Blocks.Count(block => block.Role == PdfLayoutRoles.Heading);

        if (headings > 0)
        {
            parts.Add(headings == 1 ? "1 heading" : FormattableString.Invariant($"{headings} headings"));
        }

        return string.Join(", ", parts);
    }

    private static string DescribeRole(PdfLayoutBlock block, PdfPageLayout page)
    {
        var role = block.Role switch
        {
            PdfLayoutRoles.Heading => FormattableString.Invariant($"heading {block.Level ?? 1} ({block.Style.Size:0.#}pt{(block.Style.Bold ? " bold" : string.Empty)})"),
            PdfLayoutRoles.ListItem => "list item",
            PdfLayoutRoles.FigureText => "text in a figure",
            PdfLayoutRoles.Table => "table text",
            _ => block.Role,
        };

        return page.Columns.Count > 1 && block.Column is int column && block.Role is not (PdfLayoutRoles.Header or PdfLayoutRoles.Footer)
            ? role + FormattableString.Invariant($" (column {column})")
            : role;
    }
}
