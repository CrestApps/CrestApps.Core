using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.DataIngestion;

namespace CrestApps.Core.AI.Documents.OpenXml.Services;

/// <summary>
/// Reads Word, Excel, and PowerPoint files into an <see cref="IngestionDocument"/> using the Open XML SDK.
/// </summary>
public sealed class OpenXmlIngestionDocumentReader : IngestionDocumentReader
{
    private static readonly HashSet<string> _supportedMediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
    };

    /// <summary>
    /// Reads the operation.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="identifier">The identifier.</param>
    /// <param name="mediaType">The media type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task<IngestionDocument> ReadAsync(
        Stream source,
        string identifier,
        string mediaType,
        CancellationToken cancellationToken = default)
    {
        if (!_supportedMediaTypes.Contains(mediaType))
        {
            throw new NotSupportedException($"Media type '{mediaType}' is not supported by the OpenXml reader.");
        }

        var document = new IngestionDocument(identifier);

        MemoryStream buffer = null;
        var workingStream = source;

        if (source.CanSeek)
        {
            source.Position = 0;
        }
        else
        {
            buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            workingStream = buffer;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var section = mediaType switch
            {
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ExtractWord(workingStream, cancellationToken),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ExtractExcel(workingStream, cancellationToken),
                "application/vnd.openxmlformats-officedocument.presentationml.presentation" => ExtractPowerPoint(workingStream, cancellationToken),
                _ => null,
            };

            if (section != null)
            {
                document.Sections.Add(section);
            }
        }
        finally
        {
            if (buffer != null)
            {
                await buffer.DisposeAsync();
            }
        }

        return document;
    }

    private static IngestionDocumentSection ExtractWord(Stream stream, CancellationToken cancellationToken)
    {
        using var doc = WordprocessingDocument.Open(stream, false);

        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null)
        {
            return null;
        }

        var section = new IngestionDocumentSection();

        foreach (var paragraph in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(paragraph.InnerText))
            {
                section.Elements.Add(new IngestionDocumentParagraph(paragraph.InnerText)
                {
                    Text = paragraph.InnerText,
                });
            }
        }

        return section.Elements.Count > 0 ? section : null;
    }

    private static IngestionDocumentSection ExtractExcel(Stream stream, CancellationToken cancellationToken)
    {
        using var doc = SpreadsheetDocument.Open(stream, false);

        var workbook = doc.WorkbookPart;
        if (workbook == null)
        {
            return null;
        }

        var sharedStrings = workbook.SharedStringTablePart?.SharedStringTable;
        var section = new IngestionDocumentSection();
        var values = new List<string>();

        foreach (var sheet in workbook.WorksheetParts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var data = sheet.Worksheet.GetFirstChild<SheetData>();

            if (data == null)
            {
                continue;
            }

            foreach (var row in data.Elements<Row>())
            {
                GetRowValues(row, sharedStrings, values);

                if (values.Count > 0)
                {
                    var rowText = string.Join("\t", values);
                    section.Elements.Add(new IngestionDocumentParagraph(rowText)
                    {
                        Text = rowText,
                    });
                }
            }
        }

        return section.Elements.Count > 0 ? section : null;
    }

    /// <summary>
    /// Populates reusable storage with a spreadsheet row's values while omitting trailing empty cells.
    /// </summary>
    /// <param name="row">The spreadsheet row.</param>
    /// <param name="sharedStrings">The workbook shared-string table.</param>
    /// <param name="values">The per-read reusable value storage.</param>
    private static void GetRowValues(Row row, SharedStringTable sharedStrings, List<string> values)
    {
        values.Clear();

        foreach (var cell in row.Elements<Cell>())
        {
            var columnIndex = GetColumnIndex(cell.CellReference?.Value, values.Count);

            while (values.Count < columnIndex)
            {
                values.Add(string.Empty);
            }

            values.Add(GetCellValue(cell, sharedStrings));
        }

        for (var index = values.Count - 1; index >= 0; index--)
        {
            if (!string.IsNullOrEmpty(values[index]))
            {
                break;
            }

            values.RemoveAt(index);
        }
    }

    private static int GetColumnIndex(string cellReference, int fallbackIndex)
    {
        if (string.IsNullOrEmpty(cellReference))
        {
            return fallbackIndex;
        }

        var columnIndex = 0;
        var hasColumnName = false;

        foreach (var c in cellReference)
        {
            if (!char.IsLetter(c))
            {
                break;
            }

            hasColumnName = true;
            columnIndex = (columnIndex * 26) + char.ToUpperInvariant(c) - 'A' + 1;
        }

        return hasColumnName ? columnIndex - 1 : fallbackIndex;
    }

    private static IngestionDocumentSection ExtractPowerPoint(Stream stream, CancellationToken cancellationToken)
    {
        using var doc = PresentationDocument.Open(stream, false);

        var presentation = doc.PresentationPart;
        if (presentation == null)
        {
            return null;
        }

        var section = new IngestionDocumentSection();
        var builder = new StringBuilder();

        foreach (var slide in presentation.SlideParts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            builder.Clear();

            foreach (var text in slide.Slide.Descendants<DocumentFormat.OpenXml.Drawing.Text>())
            {
                if (!string.IsNullOrWhiteSpace(text.Text))
                {
                    builder.AppendLine(text.Text);
                }
            }

            if (builder.Length > 0)
            {
                var slideText = builder.ToString().TrimEnd();
                section.Elements.Add(new IngestionDocumentParagraph(slideText)
                {
                    Text = slideText,
                });
            }
        }

        return section.Elements.Count > 0 ? section : null;
    }

    private static string GetCellValue(Cell cell, SharedStringTable table)
    {
        if (cell.DataType?.Value == CellValues.InlineString)
        {
            return cell.InlineString?.InnerText ?? string.Empty;
        }

        if (cell.CellValue == null)
        {
            return string.Empty;
        }

        var value = cell.CellValue.InnerText;

        if (cell.DataType?.Value == CellValues.SharedString &&
            int.TryParse(value, out var index) &&
            table != null)
        {
            var item = table.ChildElements.Count > index
                ? table.ChildElements[index]
                : null;

            return item?.InnerText ?? value;
        }

        if (cell.DataType?.Value == CellValues.Boolean)
        {
            return value == "1" ? "TRUE" : "FALSE";
        }

        return value;
    }
}
