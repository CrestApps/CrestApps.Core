using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Generation.Spreadsheets;
using CrestApps.Core.AI.Documents.OpenXml.Services;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;

namespace CrestApps.Core.Tests.Helpers.DocumentReaders;

public sealed class SpreadsheetMultiSheetTests
{
    private readonly SpreadsheetGeneratedFileWriter _writer = new();

    /// <summary>
    /// A workbook uploaded with several worksheets should come back with several tabs, rather than
    /// forcing a choice between them.
    /// </summary>
    [Fact]
    public async Task WriteAsync_SeveralSheets_WritesOneTabEach()
    {
        using var document = await WriteAsync(new GeneratedFileContent
        {
            Sheets =
            [
                new GeneratedSheet
                {
                    Name = "North",
                    Header = ["Account", "Amount"],
                    Rows = [["Account A", "1000"]],
                },
                new GeneratedSheet
                {
                    Name = "South",
                    Header = ["Account", "Amount"],
                    Rows = [["Account B", "2000"], ["Account C", "3000"]],
                },
            ],
        });

        var sheets = document.WorkbookPart.Workbook.Sheets.Elements<Sheet>().ToList();

        Assert.Equal(["North", "South"], sheets.Select(sheet => sheet.Name.Value));
        Assert.Equal(2, document.WorkbookPart.WorksheetParts.Count());
    }

    /// <summary>
    /// Verifies that each tab keeps its own presentation, so one sheet's currency format does not leak
    /// into another.
    /// </summary>
    [Fact]
    public async Task WriteAsync_SeveralSheets_KeepPerSheetFormatting()
    {
        using var document = await WriteAsync(new GeneratedFileContent
        {
            Sheets =
            [
                new GeneratedSheet
                {
                    Name = "Money",
                    Header = ["Amount"],
                    Rows = [["1000"]],
                    Formatting = new SpreadsheetFormatting
                    {
                        Columns = [new SpreadsheetColumnFormat { Column = "Amount", NumberFormat = SpreadsheetNumberFormat.Currency }],
                    },
                },
                new GeneratedSheet
                {
                    Name = "Plain",
                    Header = ["Amount"],
                    Rows = [["1000"]],
                },
            ],
        });

        var parts = document.WorkbookPart.WorksheetParts.ToList();
        var formats = parts
            .Select(part => GetNumberFormatCode(document, GetFirstDataCell(part).StyleIndex?.Value ?? 0))
            .ToList();

        Assert.Contains(formats, code => code is not null && code.Contains('$'));
        Assert.Contains(formats, code => code is null);
    }

    /// <summary>
    /// Two tabs may not share a name. Worksheet names are also capped in length, so two tables whose
    /// names collide once truncated must still produce a valid workbook.
    /// </summary>
    [Fact]
    public async Task WriteAsync_DuplicateSheetNames_AreMadeUnique()
    {
        using var document = await WriteAsync(new GeneratedFileContent
        {
            Sheets =
            [
                new GeneratedSheet { Name = "Summary", Header = ["A"], Rows = [["1"]] },
                new GeneratedSheet { Name = "Summary", Header = ["A"], Rows = [["2"]] },
                new GeneratedSheet { Name = "Summary", Header = ["A"], Rows = [["3"]] },
            ],
        });

        var names = document.WorkbookPart.Workbook.Sheets.Elements<Sheet>()
            .Select(sheet => sheet.Name.Value)
            .ToList();

        Assert.Equal(3, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// Verifies that merged ranges, named ranges, and protection reach the file, and that the workbook
    /// remains structurally valid with all of them present.
    /// </summary>
    [Fact]
    public async Task WriteAsync_MergesNamesAndProtection_AreWrittenAndValid()
    {
        using var document = await WriteAsync(new GeneratedFileContent
        {
            Header = ["Region", "Plan", "Actual"],
            Rows = [["North", "100", "120"], ["South", "200", "150"]],
            SpreadsheetFormatting = new SpreadsheetFormatting
            {
                SheetName = "Report",
                MergedCells = ["A1:C1"],
                NamedRanges = [new SpreadsheetNamedRange { Name = "ReportData", Range = "A1:C3" }],
                ProtectSheet = true,
            },
        });

        var worksheet = document.WorkbookPart.WorksheetParts.Single().Worksheet;

        Assert.Equal("A1:C1", worksheet.Elements<MergeCells>().Single().Elements<MergeCell>().Single().Reference.Value);
        Assert.NotNull(worksheet.Elements<SheetProtection>().SingleOrDefault());

        var defined = document.WorkbookPart.Workbook.Elements<DefinedNames>().Single().Elements<DefinedName>().Single();

        Assert.Equal("ReportData", defined.Name.Value);
        Assert.Equal("Report!$A$1:$C$3", defined.Text);

        var validator = new OpenXmlValidator();
        var errors = validator.Validate(document, TestContext.Current.CancellationToken).ToList();

        Assert.True(
            errors.Count == 0,
            "The generated workbook is not valid: " + string.Join(
                Environment.NewLine,
                errors.Select(error => $"{error.Path?.XPath}: {error.Description}")));
    }

    /// <summary>
    /// A merge referencing a row or column that does not exist makes the whole workbook unopenable, so
    /// it is dropped rather than written. Losing one visual flourish is far cheaper than losing the file.
    /// </summary>
    [Theory]
    [InlineData("A1:Z1")]
    [InlineData("A1:B99")]
    [InlineData("not a range")]
    [InlineData("A1")]
    public async Task WriteAsync_OutOfBoundsMerge_IsDropped(string range)
    {
        using var document = await WriteAsync(new GeneratedFileContent
        {
            Header = ["Region", "Plan"],
            Rows = [["North", "100"]],
            SpreadsheetFormatting = new SpreadsheetFormatting { MergedCells = [range] },
        });

        var worksheet = document.WorkbookPart.WorksheetParts.Single().Worksheet;

        Assert.Empty(worksheet.Elements<MergeCells>());
    }

    /// <summary>
    /// Verifies that an unusable defined name is skipped, since a name containing a space or starting
    /// with a digit makes the workbook fail to open.
    /// </summary>
    [Theory]
    [InlineData("Has Space")]
    [InlineData("1Leading")]
    public async Task WriteAsync_UnusableDefinedName_IsSkipped(string name)
    {
        using var document = await WriteAsync(new GeneratedFileContent
        {
            Header = ["Region"],
            Rows = [["North"]],
            SpreadsheetFormatting = new SpreadsheetFormatting
            {
                NamedRanges = [new SpreadsheetNamedRange { Name = name, Range = "A1:A2" }],
            },
        });

        Assert.Empty(document.WorkbookPart.Workbook.Elements<DefinedNames>());
    }

    /// <summary>
    /// Verifies that a column carrying a format code inherited from the source file is sized from that
    /// code, not from the raw value, so it does not render as ######.
    /// </summary>
    [Fact]
    public void Layout_InheritedFormatCode_SizesColumnFromTheCode()
    {
        var content = new GeneratedFileContent
        {
            Header = ["Amount"],
            Rows = [["837471"]],
            SpreadsheetFormatting = new SpreadsheetFormatting
            {
                Columns = [new SpreadsheetColumnFormat { Column = "Amount", FormatCode = "\"$\"#,##0.00" }],
            },
        };

        // "$837,471.00" is eleven characters; the raw value is six.
        Assert.True(SpreadsheetLayout.Create(content).Columns[0].Width >= 12);
    }

    private async Task<SpreadsheetDocument> WriteAsync(GeneratedFileContent content)
    {
        var buffer = new MemoryStream();

        await _writer.WriteAsync(content, buffer, TestContext.Current.CancellationToken);

        buffer.Position = 0;

        return SpreadsheetDocument.Open(buffer, isEditable: false);
    }

    private static Cell GetFirstDataCell(WorksheetPart part)
    {
        return part.Worksheet.GetFirstChild<SheetData>().Elements<Row>().ElementAt(1).Elements<Cell>().First();
    }

    private static string GetNumberFormatCode(SpreadsheetDocument document, uint styleIndex)
    {
        var stylesheet = document.WorkbookPart.WorkbookStylesPart.Stylesheet;
        var cellFormat = stylesheet.Elements<CellFormats>().Single().Elements<CellFormat>().ElementAt((int)styleIndex);
        var numberFormatId = cellFormat.NumberFormatId.Value;

        if (numberFormatId == 0)
        {
            return null;
        }

        return stylesheet.Elements<NumberingFormats>().SingleOrDefault()?
            .Elements<NumberingFormat>()
            .FirstOrDefault(format => format.NumberFormatId.Value == numberFormatId)?
            .FormatCode?.Value;
    }
}
