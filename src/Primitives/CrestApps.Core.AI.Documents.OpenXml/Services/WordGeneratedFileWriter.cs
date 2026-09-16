using System.Globalization;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Generation.RichText;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.OpenXml.Services;

/// <summary>
/// Writes <see cref="GeneratedFileContent"/> as an Open XML word-processing document (<c>.docx</c>).
/// <para>
/// The body text is parsed before it is laid out, so Markdown and HTML become real headings, lists,
/// quotes, code blocks, rules, tables, and emphasis. Writing the string out verbatim instead would put
/// raw markup in front of the reader, which is what the previous line-per-paragraph approach did.
/// </para>
/// </summary>
public sealed class WordGeneratedFileWriter : IGeneratedFileWriter
{
    private const string BodyFont = "Calibri";
    private const string CodeFont = "Consolas";
    private const string QuoteColor = "444444";
    private const string RuleColor = "BBBBBB";
    private const string LinkColor = "1F4E79";
    private const string HeaderShading = "EEEEEE";

    /// <summary>
    /// Writes the content as an Open XML word-processing document to the destination stream.
    /// </summary>
    /// <param name="content">The content to write.</param>
    /// <param name="destination">The destination stream.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task WriteAsync(GeneratedFileContent content, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(destination);

        using var buffer = new MemoryStream();

        using (var document = WordprocessingDocument.Create(buffer, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            var body = new Body();
            mainPart.Document = new Document(body);

            if (!string.IsNullOrWhiteSpace(content.Title))
            {
                body.Append(CreateHeading(content.Title.Trim(), level: 1));
            }

            if (!string.IsNullOrEmpty(content.Text))
            {
                foreach (var block in RichTextParser.Parse(content.Text))
                {
                    AppendBlock(body, block);
                }
            }

            if (content.HasTable)
            {
                body.Append(CreateDataTable(content));
            }

            mainPart.Document.Save();
        }

        cancellationToken.ThrowIfCancellationRequested();

        var bytes = buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length));
        await destination.WriteAsync(bytes, cancellationToken);
    }

    private static void AppendBlock(Body body, RichTextBlock block)
    {
        switch (block.Kind)
        {
            case RichTextBlockKind.Heading:
                body.Append(CreateHeading(block, block.Level));

                break;

            case RichTextBlockKind.BulletItem:
                body.Append(CreateListItem(block, "•\t"));

                break;

            case RichTextBlockKind.NumberedItem:
                body.Append(CreateListItem(block, block.Number.ToString(CultureInfo.InvariantCulture) + ".\t"));

                break;

            case RichTextBlockKind.Quote:
                body.Append(CreateQuote(block));

                break;

            case RichTextBlockKind.Code:
                body.Append(CreateCode(block));

                break;

            case RichTextBlockKind.HorizontalRule:
                body.Append(CreateHorizontalRule());

                break;

            case RichTextBlockKind.Table:
                body.Append(CreateParsedTable(block.Table));
                body.Append(new Paragraph());

                break;

            default:
                body.Append(CreateParagraph(block.Spans));

                break;
        }
    }

    private static Paragraph CreateHeading(RichTextBlock block, int level)
    {
        return CreateHeading(block.Spans, level);
    }

    private static Paragraph CreateHeading(string text, int level)
    {
        return CreateHeading([new RichTextSpan(text)], level);
    }

    private static Paragraph CreateHeading(IEnumerable<RichTextSpan> spans, int level)
    {
        // Half-point sizes step down with depth so the hierarchy the author expressed is visible.
        var halfPoints = level switch
        {
            1 => "32",
            2 => "28",
            3 => "25",
            _ => "23",
        };

        var paragraph = new Paragraph();

        paragraph.Append(CreateParagraphProperties(
            new KeepNext(),
            new SpacingBetweenLines
            {
                Before = level <= 2 ? "280" : "200",
                After = "100",
            }));

        AppendSpans(paragraph, spans, forceBold: true, fontSize: halfPoints);

        return paragraph;
    }

    private static Paragraph CreateListItem(RichTextBlock block, string marker)
    {
        var paragraph = new Paragraph();

        paragraph.Append(CreateParagraphProperties(new Indentation
        {
            Left = "454",

            // The marker hangs to the left of the wrapped text so a long item stays aligned.
            Hanging = "284",
        }));

        paragraph.Append(CreateRun(new RichTextSpan(marker)));
        AppendSpans(paragraph, block.Spans);

        return paragraph;
    }

    private static Paragraph CreateQuote(RichTextBlock block)
    {
        var paragraph = new Paragraph();

        paragraph.Append(CreateParagraphProperties(
            new ParagraphBorders(new LeftBorder
            {
                Val = BorderValues.Single,
                Size = 12,
                Color = RuleColor,
                Space = 8,
            }),
            new Indentation { Left = "454" }));

        foreach (var span in block.Spans)
        {
            paragraph.Append(CreateRun(
                new RichTextSpan(span.Text)
                {
                    Bold = span.Bold,
                    Italic = true,
                    Code = span.Code,
                    Link = span.Link,
                },
                color: QuoteColor));
        }

        return paragraph;
    }

    private static Paragraph CreateCode(RichTextBlock block)
    {
        var paragraph = new Paragraph();

        paragraph.Append(CreateParagraphProperties(
            new Shading { Val = ShadingPatternValues.Clear, Fill = "F4F4F4" },
            new Indentation { Left = "170" }));

        var lines = (block.Text ?? string.Empty).Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var run = new Run();

            run.Append(CreateRunProperties(
                new RunFonts { Ascii = CodeFont, HighAnsi = CodeFont },
                new FontSize { Val = "19" }));

            if (index > 0)
            {
                run.Append(new Break());
            }

            run.Append(new Text(lines[index].TrimEnd('\r'))
            {
                Space = SpaceProcessingModeValues.Preserve,
            });

            paragraph.Append(run);
        }

        return paragraph;
    }

    private static Paragraph CreateHorizontalRule()
    {
        var paragraph = new Paragraph();

        paragraph.Append(CreateParagraphProperties(new ParagraphBorders(new BottomBorder
        {
            Val = BorderValues.Single,
            Size = 6,
            Color = RuleColor,
            Space = 1,
        })));

        return paragraph;
    }

    private static Table CreateParsedTable(RichTextTable source)
    {
        if (source is null || source.ColumnCount == 0)
        {
            return CreateTableShell(1);
        }

        var columnCount = source.ColumnCount;
        var table = CreateTableShell(columnCount);
        var headerRow = new TableRow();

        for (var index = 0; index < columnCount; index++)
        {
            var spans = index < source.Header.Cells.Count
                ? source.Header.Cells[index].Spans
                : [];

            headerRow.Append(CreateCell(spans, isHeader: true));
        }

        table.Append(headerRow);

        foreach (var row in source.Rows)
        {
            var dataRow = new TableRow();

            for (var index = 0; index < columnCount; index++)
            {
                var spans = index < row.Cells.Count
                    ? row.Cells[index].Spans
                    : [];

                dataRow.Append(CreateCell(spans, isHeader: false));
            }

            table.Append(dataRow);
        }

        return table;
    }

    private static Table CreateDataTable(GeneratedFileContent content)
    {
        var table = CreateTableShell(content.Header.Count);
        var headerRow = new TableRow();

        foreach (var header in content.Header)
        {
            headerRow.Append(CreateCell([new RichTextSpan(header ?? string.Empty)], isHeader: true));
        }

        table.Append(headerRow);

        if (content.Rows is null)
        {
            return table;
        }

        foreach (var row in content.Rows)
        {
            var dataRow = new TableRow();

            for (var index = 0; index < content.Header.Count; index++)
            {
                var value = index < row.Count ? row[index] : string.Empty;
                dataRow.Append(CreateCell([new RichTextSpan(value ?? string.Empty)], isHeader: false));
            }

            table.Append(dataRow);
        }

        return table;
    }

    private static Table CreateTableShell(int columnCount)
    {
        var table = new Table();

        // The border elements are written top, left, bottom, right, inside-horizontal, inside-vertical.
        // The schema fixes that order and a word processor rejects any other.
        table.Append(new TableProperties(
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = RuleColor },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Color = RuleColor },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = RuleColor },
                new RightBorder { Val = BorderValues.Single, Size = 4, Color = RuleColor },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = RuleColor },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = RuleColor })));

        // A table must declare its grid before any row. Without it the document is rejected outright.
        var grid = new TableGrid();

        for (var index = 0; index < Math.Max(columnCount, 1); index++)
        {
            grid.Append(new GridColumn());
        }

        table.Append(grid);

        return table;
    }

    private static TableCell CreateCell(IEnumerable<RichTextSpan> spans, bool isHeader)
    {
        var cell = new TableCell();

        if (isHeader)
        {
            cell.Append(new TableCellProperties(new Shading
            {
                Val = ShadingPatternValues.Clear,
                Fill = HeaderShading,
            }));
        }

        cell.Append(CreateParagraph(spans, forceBold: isHeader));

        return cell;
    }

    private static Paragraph CreateParagraph(IEnumerable<RichTextSpan> spans, bool forceBold = false)
    {
        var paragraph = new Paragraph();
        AppendSpans(paragraph, spans, forceBold);

        return paragraph;
    }

    private static void AppendSpans(
        Paragraph paragraph,
        IEnumerable<RichTextSpan> spans,
        bool forceBold = false,
        string fontSize = null)
    {
        var wroteAnything = false;

        foreach (var span in spans)
        {
            if (string.IsNullOrEmpty(span.Text))
            {
                continue;
            }

            paragraph.Append(CreateRun(span, forceBold: forceBold, fontSize: fontSize));
            wroteAnything = true;
        }

        if (!wroteAnything)
        {
            paragraph.Append(CreateRun(new RichTextSpan(string.Empty)));
        }
    }

    private static Run CreateRun(
        RichTextSpan span,
        bool forceBold = false,
        string color = null,
        string fontSize = null)
    {
        var run = new Run();
        var properties = new List<OpenXmlElement>
        {
            span.Code
                ? new RunFonts { Ascii = CodeFont, HighAnsi = CodeFont }
                : new RunFonts { Ascii = BodyFont, HighAnsi = BodyFont },
        };

        if (span.Bold || forceBold)
        {
            properties.Add(new Bold());
        }

        if (span.Italic)
        {
            properties.Add(new Italic());
        }

        if (span.Strikethrough)
        {
            properties.Add(new Strike());
        }

        var runColor = color ?? (string.IsNullOrEmpty(span.Link) ? null : LinkColor);

        if (runColor is not null)
        {
            properties.Add(new Color { Val = runColor });
        }

        if (span.Code)
        {
            properties.Add(new FontSize { Val = "19" });
        }
        else if (fontSize is not null)
        {
            properties.Add(new FontSize { Val = fontSize });
        }

        if (!string.IsNullOrEmpty(span.Link))
        {
            properties.Add(new Underline { Val = UnderlineValues.Single });
        }

        run.Append(CreateRunProperties([.. properties]));

        run.Append(new Text(span.Text ?? string.Empty)
        {
            Space = SpaceProcessingModeValues.Preserve,
        });

        return run;
    }

    /// <summary>
    /// Builds run properties from elements already in schema order.
    /// <para>
    /// A word-processing document fixes the order of these elements — fonts, then weight and slant,
    /// then color, size, and underline. Appending them in any other order produces a file the word
    /// processor rejects outright, so callers pass them pre-ordered rather than appending as they go.
    /// </para>
    /// </summary>
    /// <param name="elements">The properties, in schema order.</param>
    /// <returns>The run properties.</returns>
    private static RunProperties CreateRunProperties(params OpenXmlElement[] elements)
    {
        var properties = new RunProperties();

        foreach (var element in elements)
        {
            properties.Append(element);
        }

        return properties;
    }

    /// <summary>
    /// Builds paragraph properties from elements already in schema order: keep-with-next, then borders,
    /// shading, spacing, and indentation.
    /// </summary>
    /// <param name="elements">The properties, in schema order.</param>
    /// <returns>The paragraph properties.</returns>
    private static ParagraphProperties CreateParagraphProperties(params OpenXmlElement[] elements)
    {
        var properties = new ParagraphProperties();

        foreach (var element in elements)
        {
            properties.Append(element);
        }

        return properties;
    }
}
