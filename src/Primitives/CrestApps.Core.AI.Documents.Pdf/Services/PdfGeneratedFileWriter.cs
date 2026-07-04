using CrestApps.Core.AI.Documents.Generation;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace CrestApps.Core.AI.Documents.Pdf.Services;

/// <summary>
/// Writes <see cref="GeneratedFileContent"/> as a PDF document using MigraDoc/PDFsharp. The title is
/// rendered as a heading, the body text as paragraphs (one per line), and any tabular data as a table.
/// </summary>
public sealed class PdfGeneratedFileWriter : IGeneratedFileWriter
{
    private const double UsableWidthCentimeters = 16.0;

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

        var bytes = buffer.ToArray();
        await destination.WriteAsync(bytes, cancellationToken);
    }

    private static Document BuildDocument(GeneratedFileContent content)
    {
        var document = new Document();
        var normal = document.Styles["Normal"];
        normal.Font.Name = "Arial";
        normal.Font.Size = 11;

        var section = document.AddSection();
        var hasContent = false;

        if (!string.IsNullOrWhiteSpace(content.Title))
        {
            var heading = section.AddParagraph(content.Title.Trim());
            heading.Format.Font.Bold = true;
            heading.Format.Font.Size = 16;
            heading.Format.SpaceAfter = Unit.FromPoint(12);
            hasContent = true;
        }

        if (!string.IsNullOrEmpty(content.Text))
        {
            foreach (var line in content.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                section.AddParagraph(line);
            }

            hasContent = true;
        }

        if (content.HasTable)
        {
            AddTable(section, content);
            hasContent = true;
        }

        if (!hasContent)
        {
            section.AddParagraph(string.Empty);
        }

        return document;
    }

    private static void AddTable(Section section, GeneratedFileContent content)
    {
        var columnCount = content.Header.Count;
        var table = section.AddTable();
        table.Borders.Width = 0.5;

        var columnWidth = Unit.FromCentimeter(UsableWidthCentimeters / Math.Max(columnCount, 1));

        for (var i = 0; i < columnCount; i++)
        {
            table.AddColumn(columnWidth);
        }

        var headerRow = table.AddRow();

        for (var i = 0; i < columnCount; i++)
        {
            var paragraph = headerRow.Cells[i].AddParagraph(content.Header[i] ?? string.Empty);
            paragraph.Format.Font.Bold = true;
        }

        if (content.Rows is null)
        {
            return;
        }

        foreach (var row in content.Rows)
        {
            var dataRow = table.AddRow();

            for (var i = 0; i < columnCount; i++)
            {
                var value = i < row.Count ? row[i] : string.Empty;
                dataRow.Cells[i].AddParagraph(value ?? string.Empty);
            }
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
