using System.Globalization;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Generation.RichText;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace CrestApps.Core.AI.Documents.Pdf.Services;

/// <summary>
/// Writes <see cref="GeneratedFileContent"/> as a PDF document using MigraDoc/PDFsharp.
/// <para>
/// The body text is parsed before it is laid out, so Markdown and HTML become real headings, lists,
/// quotes, code blocks, rules, tables, and emphasis. Writing the string out verbatim instead would put
/// raw markup in front of the reader, which is what the previous line-per-paragraph approach did.
/// </para>
/// </summary>
public sealed class PdfGeneratedFileWriter : IGeneratedFileWriter
{
    private const double UsableWidthCentimeters = 16.0;
    private const string BodyFont = "Arial";
    private const string CodeFont = "Courier New";

    private static readonly Color _quoteColor = new(0x44, 0x44, 0x44);
    private static readonly Color _ruleColor = new(0xBB, 0xBB, 0xBB);
    private static readonly Color _codeBackground = new(0xF4, 0xF4, 0xF4);
    private static readonly Color _headerBackground = new(0xEE, 0xEE, 0xEE);
    private static readonly Color _linkColor = new(0x1F, 0x4E, 0x79);

    private static int _fontConfigured;

    /// <summary>
    /// Writes the content as a PDF to the destination stream.
    /// </summary>
    /// <param name="content">The content to write.</param>
    /// <param name="destination">The destination stream.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task WriteAsync(GeneratedFileContent content, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(destination);

        EnsureFontConfiguration();

        var document = BuildDocument(content);
        var renderer = new PdfDocumentRenderer
        {
            Document = document,
        };

        renderer.RenderDocument();

        cancellationToken.ThrowIfCancellationRequested();

        using var buffer = new MemoryStream();
        renderer.PdfDocument.Save(buffer, closeStream: false);

        var bytes = buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length));
        await destination.WriteAsync(bytes, cancellationToken);
    }

    private static Document BuildDocument(GeneratedFileContent content)
    {
        var document = new Document();
        var normal = document.Styles["Normal"];
        normal.Font.Name = BodyFont;
        normal.Font.Size = 11;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(6);

        var section = document.AddSection();
        var hasContent = false;

        if (!string.IsNullOrWhiteSpace(content.Title))
        {
            var heading = section.AddParagraph(content.Title.Trim());
            heading.Format.Font.Bold = true;
            heading.Format.Font.Size = 18;
            heading.Format.SpaceAfter = Unit.FromPoint(14);
            hasContent = true;
        }

        if (!string.IsNullOrEmpty(content.Text))
        {
            foreach (var block in RichTextParser.Parse(content.Text))
            {
                AddBlock(section, block);
                hasContent = true;
            }
        }

        if (content.HasTable)
        {
            AddDataTable(section, content);
            hasContent = true;
        }

        if (!hasContent)
        {
            section.AddParagraph(string.Empty);
        }

        return document;
    }

    private static void AddBlock(Section section, RichTextBlock block)
    {
        switch (block.Kind)
        {
            case RichTextBlockKind.Heading:
                AddHeading(section, block);

                break;

            case RichTextBlockKind.BulletItem:
                AddListItem(section, block, "•");

                break;

            case RichTextBlockKind.NumberedItem:
                AddListItem(section, block, block.Number.ToString(CultureInfo.InvariantCulture) + ".");

                break;

            case RichTextBlockKind.Quote:
                AddQuote(section, block);

                break;

            case RichTextBlockKind.Code:
                AddCode(section, block);

                break;

            case RichTextBlockKind.HorizontalRule:
                AddHorizontalRule(section);

                break;

            case RichTextBlockKind.Table:
                AddParsedTable(section, block.Table);

                break;

            default:
                AddSpans(section.AddParagraph(), block.Spans);

                break;
        }
    }

    private static void AddHeading(Section section, RichTextBlock block)
    {
        var paragraph = section.AddParagraph();

        // Sizes step down with depth so the hierarchy the author expressed is visible at a glance.
        paragraph.Format.Font.Size = block.Level switch
        {
            1 => 16,
            2 => 14,
            3 => 12.5,
            _ => 11.5,
        };

        paragraph.Format.Font.Bold = true;
        paragraph.Format.SpaceBefore = Unit.FromPoint(block.Level <= 2 ? 14 : 10);
        paragraph.Format.SpaceAfter = Unit.FromPoint(5);
        paragraph.Format.KeepWithNext = true;

        AddSpans(paragraph, block.Spans);
    }

    private static void AddListItem(Section section, RichTextBlock block, string marker)
    {
        var paragraph = section.AddParagraph();
        paragraph.Format.LeftIndent = Unit.FromCentimeter(0.8);

        // The marker hangs to the left of the wrapped text, so a long item stays aligned under itself.
        paragraph.Format.FirstLineIndent = Unit.FromCentimeter(-0.5);
        paragraph.Format.SpaceAfter = Unit.FromPoint(3);

        paragraph.AddText(marker);
        paragraph.AddTab();

        AddSpans(paragraph, block.Spans);
    }

    private static void AddQuote(Section section, RichTextBlock block)
    {
        var paragraph = section.AddParagraph();
        paragraph.Format.LeftIndent = Unit.FromCentimeter(0.8);
        paragraph.Format.SpaceBefore = Unit.FromPoint(6);
        paragraph.Format.Font.Italic = true;
        paragraph.Format.Font.Color = _quoteColor;
        paragraph.Format.Borders.Left.Width = 2;
        paragraph.Format.Borders.Left.Color = _ruleColor;
        paragraph.Format.Borders.DistanceFromLeft = Unit.FromCentimeter(0.3);

        AddSpans(paragraph, block.Spans);
    }

    private static void AddCode(Section section, RichTextBlock block)
    {
        var paragraph = section.AddParagraph();
        paragraph.Format.Font.Name = CodeFont;
        paragraph.Format.Font.Size = 9.5;
        paragraph.Format.Shading.Color = _codeBackground;
        paragraph.Format.LeftIndent = Unit.FromCentimeter(0.3);
        paragraph.Format.SpaceBefore = Unit.FromPoint(6);
        paragraph.Format.SpaceAfter = Unit.FromPoint(6);

        var lines = (block.Text ?? string.Empty).Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                paragraph.AddLineBreak();
            }

            paragraph.AddText(lines[index].TrimEnd('\r'));
        }
    }

    private static void AddHorizontalRule(Section section)
    {
        var paragraph = section.AddParagraph();
        paragraph.Format.Borders.Bottom.Width = 0.75;
        paragraph.Format.Borders.Bottom.Color = _ruleColor;
        paragraph.Format.SpaceBefore = Unit.FromPoint(6);
        paragraph.Format.SpaceAfter = Unit.FromPoint(10);
    }

    private static void AddParsedTable(Section section, RichTextTable source)
    {
        if (source is null || source.ColumnCount == 0)
        {
            return;
        }

        var columnCount = source.ColumnCount;
        var table = CreateTable(section, columnCount);
        var headerRow = table.AddRow();
        headerRow.Shading.Color = _headerBackground;
        headerRow.HeadingFormat = true;

        for (var index = 0; index < columnCount; index++)
        {
            var paragraph = headerRow.Cells[index].AddParagraph();
            paragraph.Format.Font.Bold = true;

            if (index < source.Header.Cells.Count)
            {
                AddSpans(paragraph, source.Header.Cells[index].Spans, forceBold: true);
            }
        }

        foreach (var row in source.Rows)
        {
            var dataRow = table.AddRow();

            for (var index = 0; index < columnCount; index++)
            {
                var paragraph = dataRow.Cells[index].AddParagraph();

                if (index < row.Cells.Count)
                {
                    AddSpans(paragraph, row.Cells[index].Spans);
                }
            }
        }

        AddSpacerAfterTable(section);
    }

    /// <summary>
    /// Adds the gap that separates a table from whatever follows it. A table carries no trailing space
    /// of its own, so the next block otherwise sits flush against the last row and reads as part of it.
    /// </summary>
    /// <param name="section">The section being built.</param>
    private static void AddSpacerAfterTable(Section section)
    {
        var spacer = section.AddParagraph();
        spacer.Format.SpaceAfter = Unit.FromPoint(0);
        spacer.Format.Font.Size = 5;
    }

    private static void AddDataTable(Section section, GeneratedFileContent content)
    {
        var columnCount = content.Header.Count;
        var table = CreateTable(section, columnCount);
        var headerRow = table.AddRow();
        headerRow.Shading.Color = _headerBackground;
        headerRow.HeadingFormat = true;

        for (var index = 0; index < columnCount; index++)
        {
            var paragraph = headerRow.Cells[index].AddParagraph(content.Header[index] ?? string.Empty);
            paragraph.Format.Font.Bold = true;
        }

        if (content.Rows is null)
        {
            return;
        }

        foreach (var row in content.Rows)
        {
            var dataRow = table.AddRow();

            for (var index = 0; index < columnCount; index++)
            {
                var value = index < row.Count ? row[index] : string.Empty;
                dataRow.Cells[index].AddParagraph(value ?? string.Empty);
            }
        }
    }

    private static Table CreateTable(Section section, int columnCount)
    {
        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Borders.Color = _ruleColor;
        table.Format.Font.Size = 10;
        table.Rows.LeftIndent = 0;

        var columnWidth = Unit.FromCentimeter(UsableWidthCentimeters / Math.Max(columnCount, 1));

        for (var index = 0; index < columnCount; index++)
        {
            var column = table.AddColumn(columnWidth);
            column.Format.SpaceBefore = Unit.FromPoint(2);
            column.Format.SpaceAfter = Unit.FromPoint(2);
        }

        return table;
    }

    private static void AddSpans(Paragraph paragraph, IEnumerable<RichTextSpan> spans, bool forceBold = false)
    {
        var wroteAnything = false;

        foreach (var span in spans)
        {
            if (string.IsNullOrEmpty(span.Text))
            {
                continue;
            }

            var formatted = paragraph.AddFormattedText(span.Text);

            if (span.Bold || forceBold)
            {
                formatted.Bold = true;
            }

            if (span.Italic)
            {
                formatted.Italic = true;
            }

            if (span.Strikethrough)
            {
                // MigraDoc has no strike-through, so the run is marked the way a reader still reads as
                // removed rather than silently losing the distinction.
                formatted.Font.Color = _quoteColor;
                formatted.Italic = true;
            }

            if (span.Code)
            {
                formatted.Font.Name = CodeFont;
                formatted.Font.Size = 9.5;
            }

            if (!string.IsNullOrEmpty(span.Link))
            {
                formatted.Font.Color = _linkColor;
                formatted.Font.Underline = Underline.Single;
            }

            wroteAnything = true;
        }

        if (!wroteAnything)
        {
            // An empty paragraph still has to occupy its line, or the surrounding spacing collapses.
            paragraph.AddText(string.Empty);
        }
    }

    private static void EnsureFontConfiguration()
    {
        if (Interlocked.Exchange(ref _fontConfigured, 1) != 0)
        {
            return;
        }

        // Resolve fonts from the host operating system. On non-Windows hosts that lack a custom resolver,
        // fall back to a font discovered in the system font directories so PDF generation works out of the
        // box on Linux/macOS servers. Truly font-less environments can still register their own resolver.
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        GlobalFontSettings.UseWindowsFontsUnderWsl2 = true;

        if (!OperatingSystem.IsWindows() &&
            GlobalFontSettings.FontResolver is null &&
            SystemFontResolver.TryCreate(out var resolver))
        {
            GlobalFontSettings.FontResolver = resolver;
        }
    }
}
