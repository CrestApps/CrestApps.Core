using CrestApps.Core.AI.Documents.OpenXml.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Helpers.DocumentReaders;

public sealed class OpenXmlTabularDocumentArtifactBuilderTests
{
    private readonly OpenXmlTabularDocumentArtifactBuilder _builder = new(NullLogger<OpenXmlTabularDocumentArtifactBuilder>.Instance);

    /// <summary>
    /// Verifies that an empty worksheet produces an empty artifact.
    /// </summary>
    [Fact]
    public async Task CreateAsync_EmptyWorksheet_ReturnsEmptyArtifact()
    {
        await using var stream = CreateExcelWithSequentialRows(0, includeHeader: false);

        var artifact = await _builder.CreateAsync(
            stream,
            "test.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            TestContext.Current.CancellationToken);

        Assert.Empty(artifact.Header);
        Assert.Empty(artifact.Rows);
    }

    /// <summary>
    /// Verifies that a header-only workbook preserves the header without creating data rows.
    /// </summary>
    [Fact]
    public async Task CreateAsync_HeaderOnlyWorkbook_ReturnsHeaderWithoutRows()
    {
        await using var stream = CreateExcelWithSequentialRows(0);

        var artifact = await _builder.CreateAsync(
            stream,
            "test.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            TestContext.Current.CancellationToken);

        Assert.Equal(["Index"], artifact.Header);
        Assert.Empty(artifact.Rows);
    }

    [Fact]
    public async Task CreateAsync_SharedStringsWorkbook_ExtractsHeaderAndRows()
    {
        await using var stream = CreateExcelWithSharedStrings([["Title", "Question", "Answer"], ["Thor Weapon", "What is Thor's weapon?", "Mjolnir"],]);

        var artifact = await _builder.CreateAsync(
            stream,
            "test.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            TestContext.Current.CancellationToken);

        Assert.Equal(["Title", "Question", "Answer"], artifact.Header);
        Assert.Collection(
            artifact.Rows,
            row => Assert.Equal(["Thor Weapon", "What is Thor's weapon?", "Mjolnir"], row));
    }

    [Fact]
    public async Task CreateAsync_SparseCellsWorkbook_PreservesColumnPositions()
    {
        await using var stream = CreateExcelWithSparseCells();

        var artifact = await _builder.CreateAsync(
            stream,
            "test.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            TestContext.Current.CancellationToken);

        Assert.Equal(34, artifact.Header.Count);
        Assert.Equal("Q3_C28/What fast food or quick service restaurants have you visited?", artifact.Header[33]);
        Assert.Single(artifact.Rows);
        Assert.Equal(34, artifact.Rows[0].Count);
        Assert.Equal("1", artifact.Rows[0][33]);
    }

    [Fact]
    public async Task CreateAsync_BooleanWorkbook_ExtractsBooleanValues()
    {
        await using var stream = CreateExcelWithBooleans(true, false);

        var artifact = await _builder.CreateAsync(
            stream,
            "test.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            TestContext.Current.CancellationToken);

        Assert.Equal(["TRUE", "FALSE"], artifact.Header);
        Assert.Empty(artifact.Rows);
    }

    /// <summary>
    /// Verifies exact row preservation around the former initial-capacity boundary.
    /// </summary>
    /// <param name="rowCount">The number of data rows to read.</param>
    [Theory]
    [InlineData(4095)]
    [InlineData(4096)]
    [InlineData(4097)]
    public async Task CreateAsync_RowCountAroundInitialCapacity_PreservesEveryRow(int rowCount)
    {
        await using var stream = CreateExcelWithSequentialRows(rowCount);

        var artifact = await _builder.CreateAsync(
            stream,
            "test.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            TestContext.Current.CancellationToken);

        Assert.Equal(["Index"], artifact.Header);
        Assert.Equal(rowCount, artifact.Rows.Count);
        Assert.Equal("0", artifact.Rows[0][0]);
        Assert.Equal((rowCount - 1).ToString(), artifact.Rows[^1][0]);
    }

    /// <summary>
    /// Verifies that sequential data rows retain their source order.
    /// </summary>
    [Fact]
    public async Task CreateAsync_SequentialRows_PreservesSourceOrder()
    {
        await using var stream = CreateExcelWithSequentialRows(32);

        var artifact = await _builder.CreateAsync(
            stream,
            "test.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            TestContext.Current.CancellationToken);

        Assert.Equal(
            Enumerable.Range(0, 32).Select(index => index.ToString()),
            artifact.Rows.Select(row => row[0]));
    }

    [Fact]
    public async Task CreateAsync_MultipleWorksheets_SkipsSubsequentWorksheetHeaders()
    {
        await using var stream = CreateExcelWithMultipleSheets();

        var artifact = await _builder.CreateAsync(
            stream,
            "test.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            TestContext.Current.CancellationToken);

        Assert.Equal(["Name", "Amount"], artifact.Header);
        Assert.Collection(
            artifact.Rows,
            row => Assert.Equal(["North", "100"], row),
            row => Assert.Equal(["South", "200"], row));
    }

    /// <summary>
    /// Verifies that a canceled operation stops before worksheet rows are materialized.
    /// </summary>
    [Fact]
    public async Task CreateAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        await using var stream = CreateExcelWithSequentialRows(1);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _builder.CreateAsync(
                stream,
                "test.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                cancellationTokenSource.Token));
    }

    private static MemoryStream CreateExcelWithSharedStrings(string[][] rows)
    {
        var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var allStrings = rows.SelectMany(r => r).Distinct().ToList();
            var sstPart = workbookPart.AddNewPart<SharedStringTablePart>();
            var sst = new SharedStringTable();
            foreach (var s in allStrings)
            {
                sst.AppendChild(new SharedStringItem(new DocumentFormat.OpenXml.Spreadsheet.Text(s)));
            }

            sstPart.SharedStringTable = sst;
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            uint rowIndex = 1;
            foreach (var rowData in rows)
            {
                var row = new Row
                {
                    RowIndex = rowIndex,
                };
                var colIndex = 0;
                foreach (var cellValue in rowData)
                {
                    var cellRef = $"{(char)('A' + colIndex)}{rowIndex}";
                    var cell = new Cell
                    {
                        CellReference = cellRef,
                        DataType = CellValues.SharedString,
                        CellValue = new CellValue(allStrings.IndexOf(cellValue).ToString()),
                    };
                    row.AppendChild(cell);
                    colIndex++;
                }

                sheetData.AppendChild(row);
                rowIndex++;
            }

            worksheetPart.Worksheet = new Worksheet(sheetData);
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.AppendChild(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Sheet1",
            });
        }

        stream.Position = 0;

        return stream;
    }

    private static MemoryStream CreateExcelWithSparseCells()
    {
        var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();

            var header = new Row { RowIndex = 1 };
            header.AppendChild(new Cell
            {
                CellReference = "A1",
                DataType = CellValues.InlineString,
                InlineString = new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text("Respondent")),
            });
            header.AppendChild(new Cell
            {
                CellReference = "AH1",
                DataType = CellValues.InlineString,
                InlineString = new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text("Q3_C28/What fast food or quick service restaurants have you visited?")),
            });
            sheetData.AppendChild(header);

            var row = new Row { RowIndex = 2 };
            row.AppendChild(new Cell
            {
                CellReference = "A2",
                CellValue = new CellValue("1001"),
            });
            row.AppendChild(new Cell
            {
                CellReference = "AH2",
                CellValue = new CellValue("1"),
            });
            sheetData.AppendChild(row);

            worksheetPart.Worksheet = new Worksheet(sheetData);
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.AppendChild(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Sheet1",
            });
        }

        stream.Position = 0;

        return stream;
    }

    private static MemoryStream CreateExcelWithBooleans(params bool[] values)
    {
        var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            var row = new Row
            {
                RowIndex = 1,
            };

            for (var i = 0; i < values.Length; i++)
            {
                row.AppendChild(new Cell
                {
                    CellReference = $"{(char)('A' + i)}1",
                    DataType = CellValues.Boolean,
                    CellValue = new CellValue(values[i] ? "1" : "0"),
                });
            }

            sheetData.AppendChild(row);
            worksheetPart.Worksheet = new Worksheet(sheetData);
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.AppendChild(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Sheet1",
            });
        }

        stream.Position = 0;

        return stream;
    }

    /// <summary>
    /// Creates a workbook with an optional header and sequential one-column data rows.
    /// </summary>
    /// <param name="rowCount">The number of data rows.</param>
    /// <param name="includeHeader">Whether to include the header row.</param>
    /// <returns>The workbook stream positioned at the beginning.</returns>
    private static MemoryStream CreateExcelWithSequentialRows(
        int rowCount,
        bool includeHeader = true)
    {
        var stream = new MemoryStream();

        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();

            if (includeHeader)
            {
                sheetData.AppendChild(new Row(
                    new Cell
                    {
                        CellReference = "A1",
                        DataType = CellValues.InlineString,
                        InlineString = new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text("Index")),
                    })
                {
                    RowIndex = 1,
                });
            }

            for (var index = 0; index < rowCount; index++)
            {
                var rowIndex = index + (includeHeader ? 2 : 1);
                sheetData.AppendChild(new Row(
                    new Cell
                    {
                        CellReference = $"A{rowIndex}",
                        CellValue = new CellValue(index.ToString()),
                    })
                {
                    RowIndex = (uint)rowIndex,
                });
            }

            worksheetPart.Worksheet = new Worksheet(sheetData);
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.AppendChild(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Sheet1",
            });
        }

        stream.Position = 0;

        return stream;
    }

    private static MemoryStream CreateExcelWithMultipleSheets()
    {
        var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = doc.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());

            AppendSheet(
                workbookPart,
                sheets,
                1,
                [
                    ["Name", "Amount"],
                    ["North", "100"],
                ]);
            AppendSheet(
                workbookPart,
                sheets,
                2,
                [
                    ["Name", "Amount"],
                    ["South", "200"],
                ]);
        }

        stream.Position = 0;

        return stream;
    }

    private static void AppendSheet(
        WorkbookPart workbookPart,
        Sheets sheets,
        uint sheetId,
        string[][] rows)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();

        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var row = new Row
            {
                RowIndex = (uint)rowIndex + 1,
            };

            for (var columnIndex = 0; columnIndex < rows[rowIndex].Length; columnIndex++)
            {
                row.AppendChild(new Cell
                {
                    CellReference = $"{(char)('A' + columnIndex)}{rowIndex + 1}",
                    DataType = CellValues.InlineString,
                    InlineString = new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text(rows[rowIndex][columnIndex])),
                });
            }

            sheetData.AppendChild(row);
        }

        worksheetPart.Worksheet = new Worksheet(sheetData);
        sheets.AppendChild(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = sheetId,
            Name = $"Sheet{sheetId}",
        });
    }
}
