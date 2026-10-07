using CrestApps.Core.AI.Documents.Generation.RichText;
using CrestApps.Core.AI.Documents.Pdf.Composition;

namespace CrestApps.Core.AI.Documents.Pdf.Conversion;

/// <summary>
/// Converts text files into the blocks of a composed PDF: Markdown and HTML into headings, paragraphs,
/// lists, quotes, code, rules and tables, and plain text into its paragraphs.
/// </summary>
internal static class PdfTextConverter
{
    /// <summary>
    /// Converts Markdown or HTML.
    /// </summary>
    /// <param name="text">The file's text.</param>
    /// <returns>The blocks.</returns>
    public static PdfConversionResult ConvertMarkup(string text)
    {
        var result = new PdfConversionResult();
        var blocks = new PdfConversionBlocks(result.Blocks);

        foreach (var rich in RichTextParser.Parse(text))
        {
            switch (rich.Kind)
            {
                case RichTextBlockKind.Heading:
                    {
                        var heading = PdfInlineMarkdown.FromSpans(rich.Spans);

                        if (rich.Level <= 1)
                        {
                            result.Title ??= PlainText(rich.Spans);
                        }

                        blocks.Heading(heading, rich.Level);

                        break;
                    }

                case RichTextBlockKind.BulletItem:
                    blocks.ListItem(PdfInlineMarkdown.FromSpans(rich.Spans), Math.Max(0, rich.Level - 1), ordered: false);

                    break;

                case RichTextBlockKind.NumberedItem:
                    blocks.ListItem(PdfInlineMarkdown.FromSpans(rich.Spans), Math.Max(0, rich.Level - 1), ordered: true);

                    break;

                case RichTextBlockKind.Quote:
                    blocks.Add(new PdfBlockDefinition { Type = PdfBlockTypes.Quote, Text = PdfInlineMarkdown.FromSpans(rich.Spans) });

                    break;

                case RichTextBlockKind.Code:
                    blocks.Add(new PdfBlockDefinition { Type = PdfBlockTypes.Code, Text = rich.Text });

                    break;

                case RichTextBlockKind.HorizontalRule:
                    blocks.Add(new PdfBlockDefinition { Type = PdfBlockTypes.Rule });

                    break;

                case RichTextBlockKind.Table when rich.Table is { ColumnCount: > 0 } table:
                    {
                        var rows = new List<List<string>> { table.Header.Cells.Select(cell => PlainText(cell.Spans)).ToList() };

                        rows.AddRange(table.Rows.Select(row => row.Cells.Select(cell => PlainText(cell.Spans)).ToList()));
                        blocks.Table(rows);

                        break;
                    }

                default:
                    blocks.Paragraph(PdfInlineMarkdown.FromSpans(rich.Spans));

                    break;
            }
        }

        blocks.FlushList();

        return result;
    }

    /// <summary>
    /// Converts plain text: each run of lines between blank lines is a paragraph, its line breaks kept.
    /// </summary>
    /// <param name="text">The file's text.</param>
    /// <returns>The blocks.</returns>
    public static PdfConversionResult ConvertPlainText(string text)
    {
        var result = new PdfConversionResult();
        var blocks = new PdfConversionBlocks(result.Blocks);
        var normalized = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        foreach (var paragraph in normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // A form feed is how plain text asks for a new page.
            if (paragraph.Contains('\f', StringComparison.Ordinal))
            {
                var pieces = paragraph.Split('\f');

                for (var index = 0; index < pieces.Length; index++)
                {
                    if (index > 0)
                    {
                        blocks.PageBreak();
                    }

                    blocks.Paragraph(PdfInlineMarkdown.Escape(pieces[index].Trim()));
                }

                continue;
            }

            blocks.Paragraph(PdfInlineMarkdown.Escape(paragraph));
        }

        return result;
    }

    private static string PlainText(IEnumerable<RichTextSpan> spans)
    {
        return string.Concat(spans?.Select(span => span.Text) ?? []).Trim();
    }
}
