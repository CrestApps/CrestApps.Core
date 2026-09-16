using CrestApps.Core.AI.Documents.OpenXml.Services;
using CrestApps.Core.AI.Documents.Tabular;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

/// <summary>
/// Streams a real workbook whose columns carry number formats through the importer and asserts that
/// those formats survive into the imported schema.
/// <para>
/// This is what lets an export reproduce the presentation the upload had: a column that was currency
/// in the uploaded file comes back as currency without anyone asking for it.
/// </para>
/// </summary>
public sealed class SourceFormatInheritanceTests
{
    private const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly OpenXmlTabularWorkspaceImporter _importer = new(NullLogger<OpenXmlTabularWorkspaceImporter>.Instance);

    /// <summary>
    /// Verifies that a custom currency format on a column is carried onto the imported column.
    /// </summary>
    [Fact]
    public async Task ImportAsync_CurrencyColumn_CarriesItsSourceFormat()
    {
        using var connection = OpenConnection();
        using var stream = BuildWorkbook();

        var result = Assert.Single(await ImportAsync(stream, "money.xlsx", connection));

        var amount = Assert.Single(result.Columns, column => column.Name == "amount");

        Assert.Equal("\"$\"#,##0.00", amount.SourceFormat);
    }

    /// <summary>
    /// Verifies that a built-in percentage format is carried too, so a share column comes back as a
    /// percentage rather than a bare fraction.
    /// </summary>
    [Fact]
    public async Task ImportAsync_PercentColumn_CarriesItsSourceFormat()
    {
        using var connection = OpenConnection();
        using var stream = BuildWorkbook();

        var result = Assert.Single(await ImportAsync(stream, "money.xlsx", connection));

        var share = Assert.Single(result.Columns, column => column.Name == "share");

        Assert.Equal("0.00%", share.SourceFormat);
    }

    /// <summary>
    /// A column with no meaningful format must carry none. The general format says nothing, and
    /// inheriting it would only add noise.
    /// </summary>
    [Fact]
    public async Task ImportAsync_UnformattedColumn_CarriesNoSourceFormat()
    {
        using var connection = OpenConnection();
        using var stream = BuildWorkbook();

        var result = Assert.Single(await ImportAsync(stream, "money.xlsx", connection));

        var region = Assert.Single(result.Columns, column => column.Name == "region");

        Assert.Null(region.SourceFormat);
    }

    // Builds a workbook whose second column is currency-formatted (a custom format) and whose third is
    // percentage-formatted (a built-in format), leaving the first column unformatted.
    private static MemoryStream BuildWorkbook()
    {
        var stream = new MemoryStream();

        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = new Stylesheet(
                new NumberingFormats(new NumberingFormat
                {
                    NumberFormatId = 164U,
                    FormatCode = "\"$\"#,##0.00",
                }),
                new CellFormats(
                    new CellFormat(),
                    new CellFormat { NumberFormatId = 164U, ApplyNumberFormat = true },
                    new CellFormat { NumberFormatId = 10U, ApplyNumberFormat = true }));
            stylesPart.Stylesheet.Save();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();

            sheetData.Append(CreateTextRow(1, "region", "amount", "share"));
            sheetData.Append(CreateDataRow(2, "North", "1000.5", "0.18"));
            sheetData.Append(CreateDataRow(3, "South", "2000.25", "0.32"));

            worksheetPart.Worksheet = new Worksheet(sheetData);

            workbookPart.Workbook.AppendChild(new Sheets()).Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1U,
                Name = "Money",
            });

            workbookPart.Workbook.Save();
        }

        stream.Position = 0;

        return stream;
    }

    private static Row CreateTextRow(uint rowIndex, params string[] values)
    {
        var row = new Row { RowIndex = rowIndex };

        for (var index = 0; index < values.Length; index++)
        {
            row.Append(new Cell
            {
                CellReference = $"{(char)('A' + index)}{rowIndex}",
                DataType = CellValues.InlineString,
                InlineString = new InlineString(new Text(values[index])),
            });
        }

        return row;
    }

    private static Row CreateDataRow(uint rowIndex, string region, string amount, string share)
    {
        var row = new Row { RowIndex = rowIndex };

        row.Append(new Cell
        {
            CellReference = $"A{rowIndex}",
            DataType = CellValues.InlineString,
            InlineString = new InlineString(new Text(region)),
        });

        // Style 1 is the currency format, style 2 the percentage.
        row.Append(new Cell
        {
            CellReference = $"B{rowIndex}",
            StyleIndex = 1U,
            CellValue = new CellValue(amount),
        });

        row.Append(new Cell
        {
            CellReference = $"C{rowIndex}",
            StyleIndex = 2U,
            CellValue = new CellValue(share),
        });

        return row;
    }

    private async Task<IReadOnlyList<TabularWorkspaceImportResult>> ImportAsync(
        MemoryStream stream,
        string fileName,
        SqliteConnection connection)
    {
        TabularTableNameAllocator allocator = (worksheetName, singleWorksheet) =>
            singleWorksheet || string.IsNullOrWhiteSpace(worksheetName)
                ? Path.GetFileNameWithoutExtension(fileName)
                : worksheetName;

        return await _importer.ImportAsync(
            stream,
            fileName,
            ContentType,
            connection,
            allocator,
            TestContext.Current.CancellationToken);
    }

    private static SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        return connection;
    }
}
