using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Generation.Spreadsheets;
using CrestApps.Core.AI.Documents.OpenXml.Services;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;

namespace CrestApps.Core.Tests.Helpers.DocumentReaders;

public sealed class SpreadsheetGeneratedFileWriterTests
{
    private readonly SpreadsheetGeneratedFileWriter _writer = new();

    /// <summary>
    /// The regression this whole feature exists for: a numeric column used to be written as inline
    /// text, so the reader could not sum, sort, or chart it. Every value must land as a real number.
    /// </summary>
    [Fact]
    public async Task WriteAsync_NumericColumn_WritesNumbersNotText()
    {
        var content = new GeneratedFileContent
        {
            Header = ["Region", "Amount"],
            Rows =
            [
                ["North", "1250.50"],
                ["South", "980"],
            ],
        };

        using var document = await WriteAsync(content);
        var cells = GetDataCells(document, columnIndex: 1);

        Assert.All(cells, cell => Assert.Equal(CellValues.Number, cell.DataType.Value));
        Assert.Equal(["1250.5", "980"], cells.Select(cell => cell.CellValue.Text));
    }

    /// <summary>
    /// Verifies that an identifier keeps its leading zero instead of being silently turned into a
    /// number, which would destroy the value.
    /// </summary>
    [Fact]
    public async Task WriteAsync_LeadingZeroIdentifiers_StayText()
    {
        var content = new GeneratedFileContent
        {
            Header = ["PostalCode"],
            Rows =
            [
                ["01234"],
                ["02891"],
            ],
        };

        using var document = await WriteAsync(content);
        var cells = GetDataCells(document, columnIndex: 0);

        Assert.All(cells, cell => Assert.Equal(CellValues.InlineString, cell.DataType.Value));
        Assert.Equal(["01234", "02891"], cells.Select(cell => cell.InlineString.Text.Text));
    }

    /// <summary>
    /// Verifies that a column that mixes numbers with a label is left as text, because storing it
    /// numerically would drop the label.
    /// </summary>
    [Fact]
    public async Task WriteAsync_MixedColumn_StaysText()
    {
        var content = new GeneratedFileContent
        {
            Header = ["Value"],
            Rows =
            [
                ["100"],
                ["not available"],
            ],
        };

        using var document = await WriteAsync(content);
        var cells = GetDataCells(document, columnIndex: 0);

        Assert.All(cells, cell => Assert.Equal(CellValues.InlineString, cell.DataType.Value));
    }

    /// <summary>
    /// Verifies that a requested currency format reaches the file as a number format code applied to
    /// the column's cells, and that the underlying values stay numeric.
    /// </summary>
    [Fact]
    public async Task WriteAsync_CurrencyColumn_AppliesNumberFormatToNumericCells()
    {
        var content = new GeneratedFileContent
        {
            Header = ["Region", "Amount"],
            Rows = [["North", "1250.5"]],
            SpreadsheetFormatting = new SpreadsheetFormatting
            {
                Columns =
                [
                    new SpreadsheetColumnFormat
                    {
                        Column = "Amount",
                        NumberFormat = SpreadsheetNumberFormat.Currency,
                        Decimals = 2,
                    },
                ],
            },
        };

        using var document = await WriteAsync(content);
        var cell = GetDataCells(document, columnIndex: 1).Single();

        Assert.Equal(CellValues.Number, cell.DataType.Value);

        var formatCode = GetNumberFormatCode(document, cell.StyleIndex.Value);

        Assert.Contains("#,##0.00", formatCode, StringComparison.Ordinal);
        Assert.Contains("\"$\"", formatCode, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a source value carrying a currency symbol is stripped down to its number when the
    /// column is declared as currency, so the stored value remains calculable.
    /// </summary>
    [Fact]
    public async Task WriteAsync_DecoratedCurrencyValue_StoresBareNumber()
    {
        var content = new GeneratedFileContent
        {
            Header = ["Amount"],
            Rows = [["$1,250.50"], ["($400.00)"]],
            SpreadsheetFormatting = new SpreadsheetFormatting
            {
                Columns =
                [
                    new SpreadsheetColumnFormat
                    {
                        Column = "Amount",
                        NumberFormat = SpreadsheetNumberFormat.Currency,
                    },
                ],
            },
        };

        using var document = await WriteAsync(content);
        var cells = GetDataCells(document, columnIndex: 0);

        Assert.Equal(["1250.5", "-400"], cells.Select(cell => cell.CellValue.Text));
    }

    /// <summary>
    /// Verifies that an ISO date column is stored as a date serial and given a date format, since an
    /// unformatted serial would render as a meaningless number.
    /// </summary>
    [Fact]
    public async Task WriteAsync_IsoDateColumn_StoresSerialWithDateFormat()
    {
        var content = new GeneratedFileContent
        {
            Header = ["Posted"],
            Rows = [["2026-09-15"]],
        };

        using var document = await WriteAsync(content);
        var cell = GetDataCells(document, columnIndex: 0).Single();

        Assert.Equal(CellValues.Number, cell.DataType.Value);
        Assert.Equal(new DateTime(2026, 9, 15).ToOADate().ToString("R", System.Globalization.CultureInfo.InvariantCulture), cell.CellValue.Text);
        Assert.Contains("yyyy", GetNumberFormatCode(document, cell.StyleIndex.Value), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a computed column is appended and that its named placeholders resolve to the
    /// correct cells for each row.
    /// </summary>
    [Fact]
    public async Task WriteAsync_ComputedColumn_AppendsResolvedFormula()
    {
        var content = new GeneratedFileContent
        {
            Header = ["Planned", "Actual"],
            Rows =
            [
                ["100", "120"],
                ["200", "150"],
            ],
            SpreadsheetFormatting = new SpreadsheetFormatting
            {
                Columns =
                [
                    new SpreadsheetColumnFormat
                    {
                        Column = "Variance",
                        Formula = "={Actual}-{Planned}",
                    },
                ],
            },
        };

        using var document = await WriteAsync(content);

        Assert.Equal("Variance", GetHeaderCells(document)[2].InlineString.Text.Text);

        var cells = GetDataCells(document, columnIndex: 2);

        Assert.Equal(["B2-A2", "B3-A3"], cells.Select(cell => cell.CellFormula.Text));
    }

    /// <summary>
    /// Verifies that a formula naming a column that does not exist produces an empty cell rather than a
    /// broken reference, which would make the spreadsheet application report the file as corrupt.
    /// </summary>
    [Fact]
    public async Task WriteAsync_FormulaWithUnknownColumn_LeavesCellEmpty()
    {
        var content = new GeneratedFileContent
        {
            Header = ["Planned"],
            Rows = [["100"]],
            SpreadsheetFormatting = new SpreadsheetFormatting
            {
                Columns =
                [
                    new SpreadsheetColumnFormat
                    {
                        Column = "Variance",
                        Formula = "={Missing}-{Planned}",
                    },
                ],
            },
        };

        using var document = await WriteAsync(content);
        var cell = GetDataCells(document, columnIndex: 1).Single();

        Assert.Null(cell.CellFormula);
        Assert.Null(cell.CellValue);
    }

    /// <summary>
    /// Verifies that a total row is written as a live aggregate over the data range rather than as a
    /// pre-computed literal.
    /// </summary>
    [Fact]
    public async Task WriteAsync_TotalRow_WritesLiveAggregate()
    {
        var content = new GeneratedFileContent
        {
            Header = ["Region", "Amount"],
            Rows =
            [
                ["North", "100"],
                ["South", "200"],
            ],
            SpreadsheetFormatting = new SpreadsheetFormatting
            {
                TotalRow = new SpreadsheetTotalRow
                {
                    Label = "Grand Total",
                    Columns =
                    [
                        new SpreadsheetTotalColumn
                        {
                            Column = "Amount",
                            Function = SpreadsheetAggregateFunction.Sum,
                        },
                    ],
                },
            },
        };

        using var document = await WriteAsync(content);
        var rows = GetRows(document);
        var totalRow = rows[^1];
        var cells = totalRow.Elements<Cell>().ToList();

        Assert.Equal("Grand Total", cells[0].InlineString.Text.Text);
        Assert.Equal("SUBTOTAL(109,B2:B3)", cells[1].CellFormula.Text);
    }

    /// <summary>
    /// Verifies that a color-scale rule reaches the file with its value objects and colors in the
    /// parallel order the format requires.
    /// </summary>
    [Fact]
    public async Task WriteAsync_ColorScale_WritesGradientRule()
    {
        var content = new GeneratedFileContent
        {
            Header = ["Region", "Variance"],
            Rows = [["North", "-50"], ["South", "75"]],
            SpreadsheetFormatting = new SpreadsheetFormatting
            {
                ConditionalFormats =
                [
                    new SpreadsheetConditionalFormat
                    {
                        Column = "Variance",
                        Rule = SpreadsheetConditionalRule.ColorScale,
                        MinimumColor = "#F8696B",
                        MidpointColor = "#FFEB84",
                        MaximumColor = "#63BE7B",
                    },
                ],
            },
        };

        using var document = await WriteAsync(content);
        var worksheet = document.WorkbookPart.WorksheetParts.Single().Worksheet;
        var conditional = worksheet.Elements<ConditionalFormatting>().Single();

        Assert.Equal("B2:B3", conditional.SequenceOfReferences.InnerText);

        var colorScale = conditional.Elements<ConditionalFormattingRule>().Single().Elements<ColorScale>().Single();

        Assert.Equal(3, colorScale.Elements<ConditionalFormatValueObject>().Count());
        Assert.Equal(3, colorScale.Elements<Color>().Count());
    }

    /// <summary>
    /// Verifies that a comparison rule reaches the file with a differential format, which is how a
    /// conditional style is referenced.
    /// </summary>
    [Fact]
    public async Task WriteAsync_ComparisonRule_WritesDifferentialFormat()
    {
        var content = new GeneratedFileContent
        {
            Header = ["Amount"],
            Rows = [["-10"], ["20"]],
            SpreadsheetFormatting = new SpreadsheetFormatting
            {
                ConditionalFormats =
                [
                    new SpreadsheetConditionalFormat
                    {
                        Column = "Amount",
                        Rule = SpreadsheetConditionalRule.LessThan,
                        Value = "0",
                        Style = new SpreadsheetCellStyle
                        {
                            FontColor = "#9C0006",
                            BackgroundColor = "#FFC7CE",
                        },
                    },
                ],
            },
        };

        using var document = await WriteAsync(content);
        var rule = document.WorkbookPart.WorksheetParts.Single().Worksheet
            .Elements<ConditionalFormatting>().Single()
            .Elements<ConditionalFormattingRule>().Single();

        Assert.Equal(ConditionalFormatValues.CellIs, rule.Type.Value);
        Assert.Equal(ConditionalFormattingOperatorValues.LessThan, rule.Operator.Value);
        Assert.Equal("0", rule.Elements<Formula>().Single().Text);

        var differentials = document.WorkbookPart.WorkbookStylesPart.Stylesheet.Elements<DifferentialFormats>().Single();

        Assert.NotEmpty(differentials.Elements<DifferentialFormat>());
    }

    /// <summary>
    /// Verifies that a requested chart is embedded in the workbook and bound to the worksheet's ranges.
    /// </summary>
    [Fact]
    public async Task WriteAsync_Chart_EmbedsChartBoundToRanges()
    {
        var content = new GeneratedFileContent
        {
            Header = ["Region", "Amount"],
            Rows = [["North", "100"], ["South", "200"]],
            SpreadsheetFormatting = new SpreadsheetFormatting
            {
                SheetName = "Summary",
                Charts =
                [
                    new SpreadsheetChart
                    {
                        Kind = SpreadsheetChartKind.Bar,
                        Title = "Amount by region",
                        CategoryColumn = "Region",
                        ValueColumns = ["Amount"],
                    },
                ],
            },
        };

        using var document = await WriteAsync(content);
        var worksheetPart = document.WorkbookPart.WorksheetParts.Single();
        var drawingsPart = worksheetPart.DrawingsPart;

        Assert.NotNull(drawingsPart);

        var chartPart = drawingsPart.ChartParts.Single();
        var formulas = chartPart.ChartSpace.Descendants<DocumentFormat.OpenXml.Drawing.Charts.Formula>()
            .Select(formula => formula.Text)
            .ToList();

        Assert.Contains("Summary!$A$2:$A$3", formulas);
        Assert.Contains("Summary!$B$2:$B$3", formulas);
    }

    /// <summary>
    /// Verifies that a chart naming no plottable column is skipped rather than written as an empty
    /// drawing the reader would see as a blank box.
    /// </summary>
    [Fact]
    public async Task WriteAsync_ChartWithoutPlottableColumns_IsSkipped()
    {
        var content = new GeneratedFileContent
        {
            Header = ["Region", "Notes"],
            Rows = [["North", "review"]],
            SpreadsheetFormatting = new SpreadsheetFormatting
            {
                Charts =
                [
                    new SpreadsheetChart
                    {
                        Kind = SpreadsheetChartKind.Column,
                        CategoryColumn = "Region",
                        ValueColumns = ["Missing"],
                    },
                ],
            },
        };

        using var document = await WriteAsync(content);

        Assert.Null(document.WorkbookPart.WorksheetParts.Single().DrawingsPart);
    }

    /// <summary>
    /// The most important guarantee: a workbook using every supported feature must be structurally
    /// valid, because an invalid part makes the spreadsheet application refuse the whole file.
    /// </summary>
    [Fact]
    public async Task WriteAsync_FullyFormattedWorkbook_IsValid()
    {
        var content = new GeneratedFileContent
        {
            Header = ["Region", "Planned", "Actual", "Posted"],
            Rows =
            [
                ["North", "1000.25", "1200", "2026-09-01"],
                ["South", "2000", "1500.75", "2026-09-02"],
                ["East", "1500", "1500", "2026-09-03"],
            ],
            SpreadsheetFormatting = new SpreadsheetFormatting
            {
                SheetName = "Variance Report",
                BandedRows = true,
                Columns =
                [
                    new SpreadsheetColumnFormat
                    {
                        Column = "Planned",
                        NumberFormat = SpreadsheetNumberFormat.Currency,
                        Decimals = 2,
                    },
                    new SpreadsheetColumnFormat
                    {
                        Column = "Actual",
                        NumberFormat = SpreadsheetNumberFormat.Accounting,
                        Decimals = 2,
                    },
                    new SpreadsheetColumnFormat
                    {
                        Column = "Posted",
                        NumberFormat = SpreadsheetNumberFormat.Date,
                    },
                    new SpreadsheetColumnFormat
                    {
                        Column = "Variance",
                        Formula = "={Actual}-{Planned}",
                        NumberFormat = SpreadsheetNumberFormat.Currency,
                        NegativesInRed = true,
                        Style = new SpreadsheetCellStyle
                        {
                            Alignment = SpreadsheetHorizontalAlignment.Right,
                            Border = true,
                        },
                    },
                ],
                ConditionalFormats =
                [
                    new SpreadsheetConditionalFormat
                    {
                        Column = "Variance",
                        Rule = SpreadsheetConditionalRule.ColorScale,
                    },
                    new SpreadsheetConditionalFormat
                    {
                        Column = "Actual",
                        Rule = SpreadsheetConditionalRule.DataBar,
                        BarColor = "#638EC6",
                    },
                    new SpreadsheetConditionalFormat
                    {
                        Column = "Planned",
                        Rule = SpreadsheetConditionalRule.LessThan,
                        Value = "1200",
                        Style = new SpreadsheetCellStyle { BackgroundColor = "#FFC7CE" },
                    },
                    new SpreadsheetConditionalFormat
                    {
                        Column = "Region",
                        Rule = SpreadsheetConditionalRule.ContainsText,
                        Value = "North",
                        Style = new SpreadsheetCellStyle { Bold = true },
                    },
                ],
                TotalRow = new SpreadsheetTotalRow
                {
                    Label = "Total",
                    Columns =
                    [
                        new SpreadsheetTotalColumn { Column = "Planned", Function = SpreadsheetAggregateFunction.Sum },
                        new SpreadsheetTotalColumn { Column = "Actual", Function = SpreadsheetAggregateFunction.Sum },
                        new SpreadsheetTotalColumn { Column = "Variance", Function = SpreadsheetAggregateFunction.Sum },
                    ],
                },
                Charts =
                [
                    new SpreadsheetChart
                    {
                        Kind = SpreadsheetChartKind.Column,
                        Title = "Planned against actual",
                        CategoryColumn = "Region",
                        ValueColumns = ["Planned", "Actual"],
                    },
                    new SpreadsheetChart
                    {
                        Kind = SpreadsheetChartKind.Pie,
                        Title = "Share of actual",
                        CategoryColumn = "Region",
                        ValueColumns = ["Actual"],
                    },
                ],
            },
        };

        using var document = await WriteAsync(content);

        var validator = new OpenXmlValidator();
        var errors = validator.Validate(document, TestContext.Current.CancellationToken).ToList();

        Assert.True(
            errors.Count == 0,
            "The generated workbook is not valid: " + string.Join(
                Environment.NewLine,
                errors.Select(error => $"{error.Path?.XPath}: {error.Description}")));
    }

    /// <summary>
    /// Verifies that free-form text with no table still produces a usable workbook, preserving the
    /// behavior callers relied on before formatting existed.
    /// </summary>
    [Fact]
    public async Task WriteAsync_TextOnlyContent_WritesSingleCell()
    {
        var content = new GeneratedFileContent
        {
            Text = "No rows matched the filter.",
        };

        using var document = await WriteAsync(content);
        var cell = GetRows(document).Single().Elements<Cell>().Single();

        Assert.Equal("No rows matched the filter.", cell.InlineString.Text.Text);
    }

    private async Task<SpreadsheetDocument> WriteAsync(GeneratedFileContent content)
    {
        var buffer = new MemoryStream();

        await _writer.WriteAsync(content, buffer, TestContext.Current.CancellationToken);

        buffer.Position = 0;

        return SpreadsheetDocument.Open(buffer, isEditable: false);
    }

    private static List<Row> GetRows(SpreadsheetDocument document)
    {
        return document.WorkbookPart.WorksheetParts.Single()
            .Worksheet.GetFirstChild<SheetData>()
            .Elements<Row>()
            .ToList();
    }

    private static List<Cell> GetHeaderCells(SpreadsheetDocument document)
    {
        return GetRows(document)[0].Elements<Cell>().ToList();
    }

    private static List<Cell> GetDataCells(SpreadsheetDocument document, int columnIndex)
    {
        return GetRows(document)
            .Skip(1)
            .Select(row => row.Elements<Cell>().ElementAt(columnIndex))
            .ToList();
    }

    private static string GetNumberFormatCode(SpreadsheetDocument document, uint styleIndex)
    {
        var stylesheet = document.WorkbookPart.WorkbookStylesPart.Stylesheet;
        var cellFormat = stylesheet.Elements<CellFormats>().Single()
            .Elements<CellFormat>()
            .ElementAt((int)styleIndex);

        var numberFormatId = cellFormat.NumberFormatId.Value;

        return stylesheet.Elements<NumberingFormats>().Single()
            .Elements<NumberingFormat>()
            .Single(format => format.NumberFormatId.Value == numberFormatId)
            .FormatCode.Value;
    }
}
