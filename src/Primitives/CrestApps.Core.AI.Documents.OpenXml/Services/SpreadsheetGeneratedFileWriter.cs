using CrestApps.Core.AI.Documents.Generation;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace CrestApps.Core.AI.Documents.OpenXml.Services;

/// <summary>
/// Writes <see cref="GeneratedFileContent"/> as an Open XML spreadsheet (<c>.xlsx</c>). Tabular data
/// (header and rows) is written as worksheet cells; when only free-form text is supplied it is written
/// as a single cell so the file is still valid.
/// </summary>
public sealed class SpreadsheetGeneratedFileWriter : IGeneratedFileWriter
{
    /// <summary>
    /// Writes the content as an Open XML spreadsheet to the destination stream.
    /// </summary>
    /// <param name="content">The content to write.</param>
    /// <param name="destination">The destination stream.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task WriteAsync(GeneratedFileContent content, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(destination);

        using var buffer = new MemoryStream();

        using (var document = SpreadsheetDocument.Create(buffer, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            worksheetPart.Worksheet = new Worksheet(sheetData);

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1U,
                Name = "Sheet1",
            });

            WriteRows(sheetData, content);

            workbookPart.Workbook.Save();
        }

        cancellationToken.ThrowIfCancellationRequested();

        buffer.Position = 0;
        await buffer.CopyToAsync(destination, cancellationToken);
    }

    private static void WriteRows(SheetData sheetData, GeneratedFileContent content)
    {
        if (content.HasTable)
        {
            sheetData.Append(CreateRow(content.Header));

            if (content.Rows is not null)
            {
                foreach (var row in content.Rows)
                {
                    sheetData.Append(CreateRow(row));
                }
            }

            return;
        }

        if (!string.IsNullOrEmpty(content.Text))
        {
            sheetData.Append(CreateRow([content.Text]));
        }
    }

    private static Row CreateRow(IReadOnlyList<string> values)
    {
        var row = new Row();

        foreach (var value in values)
        {
            row.Append(new Cell
            {
                DataType = CellValues.InlineString,
                InlineString = new InlineString(new Text(value ?? string.Empty)
                {
                    Space = SpaceProcessingModeValues.Preserve,
                }),
            });
        }

        return row;
    }
}
