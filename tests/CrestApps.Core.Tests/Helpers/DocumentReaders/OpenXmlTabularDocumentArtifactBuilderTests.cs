using CrestApps.Core.AI.Documents.OpenXml.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.Tests.Helpers.DocumentReaders;

public sealed class OpenXmlTabularDocumentArtifactBuilderTests
{
    private readonly OpenXmlTabularDocumentArtifactBuilder _builder = new(NullLogger<OpenXmlTabularDocumentArtifactBuilder>.Instance);

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
