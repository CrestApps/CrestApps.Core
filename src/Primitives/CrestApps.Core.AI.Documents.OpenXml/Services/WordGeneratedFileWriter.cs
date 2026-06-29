using CrestApps.Core.AI.Documents.Generation;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.AI.Documents.OpenXml.Services;

/// <summary>
/// Writes <see cref="GeneratedFileContent"/> as an Open XML word-processing document (<c>.docx</c>).
/// The title is rendered as a heading, the body text as paragraphs (one per line), and any tabular data
/// as a table.
/// </summary>
public sealed class WordGeneratedFileWriter : IGeneratedFileWriter
{
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
                body.Append(CreateParagraph(content.Title.Trim(), bold: true));
            }

            if (!string.IsNullOrEmpty(content.Text))
            {
                foreach (var line in content.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
                {
                    body.Append(CreateParagraph(line, bold: false));
                }
            }

            if (content.HasTable)
            {
                body.Append(CreateTable(content));
            }

            mainPart.Document.Save();
        }

        cancellationToken.ThrowIfCancellationRequested();

        var bytes = buffer.ToArray();
        await destination.WriteAsync(bytes, cancellationToken);
    }

    private static Paragraph CreateParagraph(string text, bool bold)
    {
        var run = new Run();

        if (bold)
        {
            run.Append(new RunProperties(new Bold()));
        }

        run.Append(new Text(text ?? string.Empty)
        {
            Space = SpaceProcessingModeValues.Preserve,
        });

        return new Paragraph(run);
    }

    private static Table CreateTable(GeneratedFileContent content)
    {
        var table = new Table();

        table.Append(new TableProperties(
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 },
                new LeftBorder { Val = BorderValues.Single, Size = 4 },
                new RightBorder { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));

        table.Append(CreateTableRow(content.Header));

        if (content.Rows is not null)
        {
            foreach (var row in content.Rows)
            {
                table.Append(CreateTableRow(row));
            }
        }

        return table;
    }

    private static TableRow CreateTableRow(IReadOnlyList<string> values)
    {
        var row = new TableRow();

        foreach (var value in values)
        {
            row.Append(new TableCell(CreateParagraph(value ?? string.Empty, bold: false)));
        }

        return row;
    }
}
