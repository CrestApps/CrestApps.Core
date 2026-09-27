using System.Globalization;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using UglyToad.PdfPig.Content;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Reads a PDF's text page by page in reading order — as running text, as lines or as single words — with
/// the positions and fonts a layout question needs, so the model quotes what the page says rather than what
/// the file happens to draw first.
/// </summary>
internal sealed class ExtractPdfTextTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.ExtractPdfText;

    private const string NoTextNote = "(no extractable text — the page may be scanned; ocr_pdf can read it)";

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Pages}},
            {{PdfToolSchemas.Password}},
            "format": {
              "type": "string",
              "enum": ["text", "lines", "words"],
              "description": "How the text is returned: 'text' (default) as paragraphs in reading order; 'lines' one entry per line; 'words' one entry per word."
            },
            "include_positions": {
              "type": "boolean",
              "description": "Whether each entry carries its box (x, y, w, h in points from the page's top-left corner). Defaults to true for 'lines' and 'words', false for 'text'."
            },
            "include_fonts": {
              "type": "boolean",
              "description": "Whether each entry carries its font name, size and weight. Defaults to false."
            }
          },
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtractPdfTextTool"/> class.
    /// </summary>
    public ExtractPdfTextTool()
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
    public override string Description => "Extracts the text of a PDF page by page in reading order (columns read down, not across). Use it to read or quote a document, or with format 'lines'/'words' and include_positions/include_fonts to see where text is and how it is set. Pages without a text layer are flagged so ocr_pdf can read them. Long documents are returned in parts; the answer says which pages to ask for next.";

    /// <summary>
    /// Extracts the text.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The conversation's PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The text, page by page.</returns>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var format = (arguments.GetString("format") ?? "text").Trim().ToLowerInvariant();

        if (format is not ("text" or "lines" or "words"))
        {
            throw new PdfToolException($"\"{format}\" is not a text format. Use 'text', 'lines' or 'words'.");
        }

        var positions = arguments.GetBoolean("include_positions") ?? format != "text";
        var fonts = arguments.GetBoolean("include_fonts") ?? false;

        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var bytes = await context.ReadPdfAsync(source, cancellationToken);
        using var pdf = PdfFiles.OpenForReading(bytes, arguments.GetString("password"));
        var pages = PdfPageRange.Parse(arguments.GetPages(), pdf.NumberOfPages);

        var writer = new PdfResponseWriter(context.Options.MaxToolResponseCharacters);

        writer.Line($"\"{source.Name}\" has {pdf.NumberOfPages} page(s). Text of {DescribePages(pages)}, as {format}, in reading order.");

        if (positions)
        {
            writer.Line("Boxes are x, y, w, h in points from the top-left corner of the page.");
        }

        var stoppedAt = 0;
        var partial = false;
        var empty = new List<int>();

        foreach (var number in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = pdf.GetPage(number);
            var lines = Render(page, format, positions, fonts);

            if (lines.Count == 0)
            {
                empty.Add(number);
                lines.Add(NoTextNote);
            }

            if (!writer.TryLine(string.Empty) || !writer.TryLine(FormattableString.Invariant($"--- Page {number} ---")))
            {
                stoppedAt = number;

                break;
            }

            var written = 0;

            foreach (var line in lines)
            {
                if (!writer.TryLine(line))
                {
                    break;
                }

                written++;
            }

            if (written < lines.Count)
            {
                stoppedAt = number;
                partial = written > 0;

                break;
            }
        }

        if (stoppedAt > 0)
        {
            var remaining = pages.Where(page => page >= stoppedAt).ToList();

            writer.Line();
            writer.Line(partial
                ? $"[Stopped part way through page {stoppedAt} to stay within the answer size. Ask for pages \"{PdfPageRange.Describe(remaining)}\" to continue; a single long page reads more compactly as format 'text'.]"
                : $"[Stopped before page {stoppedAt} to stay within the answer size. Ask for pages \"{PdfPageRange.Describe(remaining)}\" to continue.]");
        }

        if (empty.Count > 0)
        {
            writer.Line();
            writer.Line($"Pages without extractable text: {PdfPageRange.Describe(empty)}. They may be scanned images; ocr_pdf can read them.");
        }

        return writer.ToString();
    }

    private static string DescribePages(List<int> pages)
    {
        return pages.Count == 1
            ? "page " + pages[0].ToString(CultureInfo.InvariantCulture)
            : "pages " + PdfPageRange.Describe(pages);
    }

    private static List<string> Render(Page page, string format, bool positions, bool fonts)
    {
        var lines = new List<string>();
        var blocks = PdfPageText.GetBlocks(page);

        foreach (var block in blocks)
        {
            switch (format)
            {
                case "words":
                    foreach (var line in block.TextLines)
                    {
                        foreach (var word in line.Words)
                        {
                            if (!string.IsNullOrWhiteSpace(word.Text))
                            {
                                lines.Add(Entry(word.Text, page, PdfBox.From(word.BoundingBox), PdfTextStyle.Of(word.Letters), positions, fonts));
                            }
                        }
                    }

                    break;

                case "lines":
                    foreach (var line in block.TextLines)
                    {
                        var text = PdfTextPatterns.OneLine(line.Text);

                        if (text.Length > 0)
                        {
                            lines.Add(Entry(text, page, PdfBox.From(line.BoundingBox), PdfTextStyle.Of(line.Words.SelectMany(word => word.Letters)), positions, fonts));
                        }
                    }

                    break;

                default:
                    {
                        if (lines.Count > 0)
                        {
                            lines.Add(string.Empty);
                        }

                        var first = true;

                        foreach (var line in block.TextLines)
                        {
                            var text = PdfTextPatterns.OneLine(line.Text);

                            if (text.Length == 0)
                            {
                                continue;
                            }

                            // In running text the box and font belong to the paragraph, so they lead it.
                            if (first && (positions || fonts))
                            {
                                var letters = block.TextLines.SelectMany(textLine => textLine.Words).SelectMany(word => word.Letters);

                                text = "[" + Annotation(page, PdfBox.From(block.BoundingBox), PdfTextStyle.Of(letters), positions, fonts) + "] " + text;
                            }

                            first = false;
                            lines.Add(text);
                        }

                        break;
                    }
            }
        }

        return lines;
    }

    private static string Entry(string text, Page page, PdfBox box, PdfTextStyle style, bool positions, bool fonts)
    {
        return positions || fonts
            ? text + "  [" + Annotation(page, box, style, positions, fonts) + "]"
            : text;
    }

    private static string Annotation(Page page, PdfBox box, PdfTextStyle style, bool positions, bool fonts)
    {
        if (positions && fonts)
        {
            return box.Describe(page) + "; " + style.Describe();
        }

        return positions
            ? box.Describe(page)
            : style.Describe();
    }
}
