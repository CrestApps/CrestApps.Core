using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Generation.Spreadsheets;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace CrestApps.Core.AI.Documents.OpenXml.Services;

/// <summary>
/// Writes <see cref="GeneratedFileContent"/> as an Open XML spreadsheet (<c>.xlsx</c>).
/// <para>
/// Values are stored using their real types: a numeric column is written as numbers and a date column
/// as date serials, so the reader can sum, sort, filter, and chart the result. Only a column that
/// genuinely holds text is written as text. When the content carries a
/// <see cref="GeneratedFileContent.SpreadsheetFormatting"/> spec, the sheet also gets number formats,
/// styling, conditional formatting, live formulas, a total row, and embedded charts.
/// </para>
/// </summary>
public sealed class SpreadsheetGeneratedFileWriter : IGeneratedFileWriter
{
    private static readonly SpreadsheetCellStyle _defaultHeaderStyle = new()
    {
        Bold = true,
        FontColor = "#FFFFFF",
        BackgroundColor = "#1F4E79",
        Alignment = SpreadsheetHorizontalAlignment.Center,
        WrapText = true,
    };

    private static readonly SpreadsheetCellStyle _defaultTotalStyle = new()
    {
        Bold = true,
        Border = true,
    };

    private const string DefaultBandColor = "#F2F2F2";

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
            var styles = new SpreadsheetStyleBuilder();

            var worksheet = content.HasTable
                ? BuildTableWorksheet(content, styles, worksheetPart, cancellationToken)
                : BuildTextWorksheet(content);

            worksheetPart.Worksheet = worksheet;

            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = styles.Build();
            stylesPart.Stylesheet.Save();

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1U,
                Name = content.HasTable
                    ? SpreadsheetLayout.Create(content).SheetName
                    : "Sheet1",
            });

            // Formulas are written without a cached result, so the workbook must recalculate when it is
            // opened; without this the reader sees empty cells until they force a recalculation.
            workbookPart.Workbook.AppendChild(new CalculationProperties
            {
                CalculationId = 0U,
                FullCalculationOnLoad = true,
            });

            workbookPart.Workbook.Save();
        }

        cancellationToken.ThrowIfCancellationRequested();

        buffer.Position = 0;
        await buffer.CopyToAsync(destination, cancellationToken);
    }

    private static Worksheet BuildTextWorksheet(GeneratedFileContent content)
    {
        var sheetData = new SheetData();

        if (!string.IsNullOrEmpty(content.Text))
        {
            var row = new Row();
            row.Append(CreateTextCell(content.Text, styleIndex: 0));
            sheetData.Append(row);
        }

        return new Worksheet(sheetData);
    }

    private static Worksheet BuildTableWorksheet(
        GeneratedFileContent content,
        SpreadsheetStyleBuilder styles,
        WorksheetPart worksheetPart,
        CancellationToken cancellationToken)
    {
        var layout = SpreadsheetLayout.Create(content);
        var formatting = layout.Formatting;
        var worksheet = new Worksheet();

        if (formatting.FreezeHeader ?? true)
        {
            worksheet.Append(BuildFrozenHeaderView());
        }

        worksheet.Append(BuildColumnWidths(layout));
        worksheet.Append(BuildSheetData(layout, styles, cancellationToken));

        if (formatting.AutoFilter != false && layout.Columns.Count > 0)
        {
            worksheet.Append(new AutoFilter
            {
                Reference = $"A{SpreadsheetLayout.HeaderRowNumber}:{SpreadsheetFormula.ToCellReference(layout.Columns.Count - 1, layout.LastDataRowNumber)}",
            });
        }

        foreach (var conditionalFormatting in BuildConditionalFormatting(layout, styles))
        {
            worksheet.Append(conditionalFormatting);
        }

        var drawing = SpreadsheetChartBuilder.TryBuildCharts(layout, worksheetPart);

        if (drawing is not null)
        {
            worksheet.Append(drawing);
        }

        return worksheet;
    }

    private static SheetViews BuildFrozenHeaderView()
    {
        return new SheetViews(new SheetView(
            new Pane
            {
                VerticalSplit = 1D,
                TopLeftCell = "A2",
                ActivePane = PaneValues.BottomLeft,
                State = PaneStateValues.Frozen,
            },
            new Selection
            {
                Pane = PaneValues.BottomLeft,
            })
        {
            TabSelected = true,
            WorkbookViewId = 0U,
        });
    }

    private static Columns BuildColumnWidths(SpreadsheetLayout layout)
    {
        var columns = new Columns();

        foreach (var column in layout.Columns)
        {
            columns.Append(new Column
            {
                Min = (uint)(column.Index + 1),
                Max = (uint)(column.Index + 1),
                Width = column.Width,
                CustomWidth = true,
            });
        }

        return columns;
    }

    private static SheetData BuildSheetData(
        SpreadsheetLayout layout,
        SpreadsheetStyleBuilder styles,
        CancellationToken cancellationToken)
    {
        var formatting = layout.Formatting;
        var sheetData = new SheetData();

        var headerStyle = formatting.StyleHeader != false
            ? formatting.HeaderStyle ?? _defaultHeaderStyle
            : formatting.HeaderStyle;

        var headerStyleIndex = headerStyle is { IsEmpty: false }
            ? styles.GetCellFormat(headerStyle, numberFormatCode: null)
            : 0U;

        var headerRow = new Row { RowIndex = (uint)SpreadsheetLayout.HeaderRowNumber };

        foreach (var column in layout.Columns)
        {
            var cell = CreateTextCell(column.Name, headerStyleIndex);
            cell.CellReference = SpreadsheetFormula.ToCellReference(column.Index, SpreadsheetLayout.HeaderRowNumber);
            headerRow.Append(cell);
        }

        sheetData.Append(headerRow);

        // Each column's data cells share one style, so the index is resolved once rather than per cell.
        var dataStyleIndexes = new uint[layout.Columns.Count];

        for (var index = 0; index < layout.Columns.Count; index++)
        {
            var column = layout.Columns[index];
            dataStyleIndexes[index] = styles.GetCellFormat(column.Style, column.NumberFormatCode);
        }

        for (var rowIndex = 0; rowIndex < layout.Rows.Count; rowIndex++)
        {
            if ((rowIndex & 0x3FF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var rowNumber = SpreadsheetLayout.FirstDataRowNumber + rowIndex;
            var values = layout.Rows[rowIndex];
            var row = new Row { RowIndex = (uint)rowNumber };

            foreach (var column in layout.Columns)
            {
                var value = !column.IsComputed && values is not null && column.Index < values.Count
                    ? values[column.Index]
                    : null;

                row.Append(CreateDataCell(value, column, dataStyleIndexes[column.Index], rowNumber, layout));
            }

            sheetData.Append(row);
        }

        if (layout.HasTotalRow)
        {
            sheetData.Append(BuildTotalRow(layout, styles));
        }

        return sheetData;
    }

    private static Row BuildTotalRow(SpreadsheetLayout layout, SpreadsheetStyleBuilder styles)
    {
        var totalRow = layout.Formatting.TotalRow;
        var style = totalRow.Style ?? _defaultTotalStyle;
        var rowNumber = layout.TotalRowNumber;
        var row = new Row { RowIndex = (uint)rowNumber };

        foreach (var column in layout.Columns)
        {
            var aggregate = FindAggregate(totalRow, column.Name);

            // The aggregate keeps the column's own number format, so a currency column's total still
            // reads as currency instead of reverting to a bare number.
            var styleIndex = styles.GetCellFormat(style, aggregate is null && column.Index > 0
                ? null
                : column.NumberFormatCode);

            Cell cell;

            if (aggregate is not null)
            {
                var range = SpreadsheetFormula.ToColumnRange(column.Index, SpreadsheetLayout.FirstDataRowNumber, layout.LastDataRowNumber);

                cell = new Cell
                {
                    CellFormula = new CellFormula(SpreadsheetFormula.ToAggregate(aggregate.Function, range)),
                    StyleIndex = styleIndex,
                };
            }
            else if (column.Index == 0)
            {
                cell = CreateTextCell(
                    string.IsNullOrWhiteSpace(totalRow.Label) ? "Total" : totalRow.Label,
                    styleIndex);
            }
            else
            {
                cell = new Cell { StyleIndex = styleIndex };
            }

            cell.CellReference = SpreadsheetFormula.ToCellReference(column.Index, rowNumber);
            row.Append(cell);
        }

        return row;
    }

    private static SpreadsheetTotalColumn FindAggregate(SpreadsheetTotalRow totalRow, string columnName)
    {
        if (totalRow.Columns is null)
        {
            return null;
        }

        foreach (var candidate in totalRow.Columns)
        {
            if (candidate is not null && SpreadsheetFormatting.NameMatches(candidate.Column, columnName))
            {
                return candidate;
            }
        }

        return null;
    }

    private static Cell CreateDataCell(
        string value,
        SpreadsheetLayoutColumn column,
        uint styleIndex,
        int rowNumber,
        SpreadsheetLayout layout)
    {
        var cell = new Cell
        {
            StyleIndex = styleIndex,
            CellReference = SpreadsheetFormula.ToCellReference(column.Index, rowNumber),
        };

        if (column.Formula is not null)
        {
            var formula = SpreadsheetFormula.Resolve(
                column.Formula,
                rowNumber,
                layout.TryGetColumnIndex);

            if (formula is not null)
            {
                cell.CellFormula = new CellFormula(formula);

                return cell;
            }

            // An unresolvable formula is left as an empty cell. Writing a broken reference would make
            // the spreadsheet application report the whole file as corrupt.
            return cell;
        }

        if (string.IsNullOrEmpty(value))
        {
            return cell;
        }

        switch (column.Kind)
        {
            case SpreadsheetDataKind.Number when SpreadsheetValue.TryParseNumber(value, out var number):
                cell.DataType = CellValues.Number;
                cell.CellValue = new CellValue(SpreadsheetNumberFormatCode.ToInvariant(number));

                return cell;

            case SpreadsheetDataKind.Date when SpreadsheetValue.TryParseDate(value, out var serial):
                cell.DataType = CellValues.Number;
                cell.CellValue = new CellValue(SpreadsheetNumberFormatCode.ToInvariant(serial));

                return cell;

            case SpreadsheetDataKind.Boolean when bool.TryParse(value, out var flag):
                cell.DataType = CellValues.Boolean;
                cell.CellValue = new CellValue(flag ? "1" : "0");

                return cell;
        }

        // A value that does not fit the column's type still has to reach the file. Writing it as text
        // keeps the data intact instead of dropping the cell.
        ApplyText(cell, value);

        return cell;
    }

    private static Cell CreateTextCell(string value, uint styleIndex)
    {
        var cell = new Cell { StyleIndex = styleIndex };
        ApplyText(cell, value ?? string.Empty);

        return cell;
    }

    private static void ApplyText(Cell cell, string value)
    {
        cell.DataType = CellValues.InlineString;
        cell.CellValue = null;
        cell.InlineString = new InlineString(new Text(value)
        {
            Space = SpaceProcessingModeValues.Preserve,
        });
    }

    private static List<ConditionalFormatting> BuildConditionalFormatting(
        SpreadsheetLayout layout,
        SpreadsheetStyleBuilder styles)
    {
        var results = new List<ConditionalFormatting>();

        if (layout.Rows.Count == 0)
        {
            return results;
        }

        var priority = 1;
        var formatting = layout.Formatting;

        if (formatting.BandedRows == true && layout.Columns.Count > 0)
        {
            var bandStyle = new SpreadsheetCellStyle
            {
                BackgroundColor = string.IsNullOrWhiteSpace(formatting.BandColor)
                    ? DefaultBandColor
                    : formatting.BandColor,
            };

            var range = $"A{SpreadsheetLayout.FirstDataRowNumber}:{SpreadsheetFormula.ToCellReference(layout.Columns.Count - 1, layout.LastDataRowNumber)}";

            results.Add(BuildConditionalFormatting(range, new ConditionalFormattingRule(new Formula("MOD(ROW(),2)=0"))
            {
                Type = ConditionalFormatValues.Expression,
                FormatId = styles.GetDifferentialFormat(bandStyle),
                Priority = priority++,
            }));
        }

        if (formatting.ConditionalFormats is null)
        {
            return results;
        }

        foreach (var conditional in formatting.ConditionalFormats)
        {
            if (conditional is null)
            {
                continue;
            }

            var column = layout.FindColumn(conditional.Column);

            if (column is null)
            {
                continue;
            }

            var range = SpreadsheetFormula.ToColumnRange(column.Index, SpreadsheetLayout.FirstDataRowNumber, layout.LastDataRowNumber);
            var rule = BuildRule(conditional, range, styles, priority++);

            if (rule is not null)
            {
                results.Add(BuildConditionalFormatting(range, rule));
            }
        }

        return results;
    }

    private static ConditionalFormatting BuildConditionalFormatting(string range, ConditionalFormattingRule rule)
    {
        return new ConditionalFormatting(rule)
        {
            SequenceOfReferences = new ListValue<StringValue> { InnerText = range },
        };
    }

    private static ConditionalFormattingRule BuildRule(
        SpreadsheetConditionalFormat conditional,
        string range,
        SpreadsheetStyleBuilder styles,
        int priority)
    {
        switch (conditional.Rule)
        {
            case SpreadsheetConditionalRule.ColorScale:
                return BuildColorScaleRule(conditional, priority);

            case SpreadsheetConditionalRule.DataBar:
                return new ConditionalFormattingRule(new DataBar(
                    new ConditionalFormatValueObject { Type = ConditionalFormatValueObjectValues.Min },
                    new ConditionalFormatValueObject { Type = ConditionalFormatValueObjectValues.Max },
                    new Color { Rgb = ResolveColor(conditional.BarColor, "#638EC6") }))
                {
                    Type = ConditionalFormatValues.DataBar,
                    Priority = priority,
                };

            case SpreadsheetConditionalRule.IconSet:
                return new ConditionalFormattingRule(new IconSet(
                    new ConditionalFormatValueObject { Type = ConditionalFormatValueObjectValues.Percent, Val = "0" },
                    new ConditionalFormatValueObject { Type = ConditionalFormatValueObjectValues.Percent, Val = "33" },
                    new ConditionalFormatValueObject { Type = ConditionalFormatValueObjectValues.Percent, Val = "67" })
                {
                    IconSetValue = ResolveIconSet(conditional.IconSet),
                })
                {
                    Type = ConditionalFormatValues.IconSet,
                    Priority = priority,
                };

            case SpreadsheetConditionalRule.DuplicateValues:
                return new ConditionalFormattingRule
                {
                    Type = ConditionalFormatValues.DuplicateValues,
                    FormatId = styles.GetDifferentialFormat(conditional.Style),
                    Priority = priority,
                };

            case SpreadsheetConditionalRule.ContainsText:
                if (string.IsNullOrEmpty(conditional.Value))
                {
                    return null;
                }

                var firstCell = range.Split(':', 2)[0];
                var escaped = conditional.Value.Replace("\"", "\"\"", StringComparison.Ordinal);

                return new ConditionalFormattingRule(new Formula($"NOT(ISERROR(SEARCH(\"{escaped}\",{firstCell})))"))
                {
                    Type = ConditionalFormatValues.ContainsText,
                    Operator = ConditionalFormattingOperatorValues.ContainsText,
                    Text = conditional.Value,
                    FormatId = styles.GetDifferentialFormat(conditional.Style),
                    Priority = priority,
                };

            default:
                return BuildComparisonRule(conditional, styles, priority);
        }
    }

    private static ConditionalFormattingRule BuildColorScaleRule(SpreadsheetConditionalFormat conditional, int priority)
    {
        var colorScale = new ColorScale();

        colorScale.Append(new ConditionalFormatValueObject { Type = ConditionalFormatValueObjectValues.Min });

        var hasMidpoint = SpreadsheetStyleBuilder.TryParseColor(conditional.MidpointColor, out _);

        if (hasMidpoint)
        {
            colorScale.Append(new ConditionalFormatValueObject
            {
                Type = ConditionalFormatValueObjectValues.Percentile,
                Val = "50",
            });
        }

        colorScale.Append(new ConditionalFormatValueObject { Type = ConditionalFormatValueObjectValues.Max });

        // The value objects and the colors are two parallel sequences, so every color must follow every
        // value object rather than being interleaved with them.
        colorScale.Append(new Color { Rgb = ResolveColor(conditional.MinimumColor, "#F8696B") });

        if (hasMidpoint)
        {
            colorScale.Append(new Color { Rgb = ResolveColor(conditional.MidpointColor, "#FFEB84") });
        }

        colorScale.Append(new Color { Rgb = ResolveColor(conditional.MaximumColor, "#63BE7B") });

        return new ConditionalFormattingRule(colorScale)
        {
            Type = ConditionalFormatValues.ColorScale,
            Priority = priority,
        };
    }

    private static ConditionalFormattingRule BuildComparisonRule(
        SpreadsheetConditionalFormat conditional,
        SpreadsheetStyleBuilder styles,
        int priority)
    {
        if (string.IsNullOrWhiteSpace(conditional.Value))
        {
            return null;
        }

        var (op, needsSecondValue) = conditional.Rule switch
        {
            SpreadsheetConditionalRule.GreaterThan => (ConditionalFormattingOperatorValues.GreaterThan, false),
            SpreadsheetConditionalRule.LessThan => (ConditionalFormattingOperatorValues.LessThan, false),
            SpreadsheetConditionalRule.EqualTo => (ConditionalFormattingOperatorValues.Equal, false),
            SpreadsheetConditionalRule.Between => (ConditionalFormattingOperatorValues.Between, true),
            _ => (ConditionalFormattingOperatorValues.GreaterThan, false),
        };

        if (needsSecondValue && string.IsNullOrWhiteSpace(conditional.SecondValue))
        {
            return null;
        }

        var rule = new ConditionalFormattingRule
        {
            Type = ConditionalFormatValues.CellIs,
            Operator = op,
            FormatId = styles.GetDifferentialFormat(conditional.Style),
            Priority = priority,
        };

        rule.Append(new Formula(FormatComparisonValue(conditional.Value)));

        if (needsSecondValue)
        {
            rule.Append(new Formula(FormatComparisonValue(conditional.SecondValue)));
        }

        return rule;
    }

    private static string FormatComparisonValue(string value)
    {
        var trimmed = value.Trim();

        // A numeric bound is compared as a number; anything else has to be quoted or the rule is read
        // as a broken formula and silently ignored.
        return SpreadsheetValue.LooksNumeric(trimmed, out _)
            ? trimmed
            : $"\"{trimmed.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string ResolveColor(string value, string fallback)
    {
        if (SpreadsheetStyleBuilder.TryParseColor(value, out var color))
        {
            return color;
        }

        return SpreadsheetStyleBuilder.TryParseColor(fallback, out var defaultColor)
            ? defaultColor
            : null;
    }

    private static IconSetValues ResolveIconSet(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return IconSetValues.ThreeTrafficLights1;
        }

        return name.Trim() switch
        {
            "3Arrows" => IconSetValues.ThreeArrows,
            "3ArrowsGray" => IconSetValues.ThreeArrowsGray,
            "3Flags" => IconSetValues.ThreeFlags,
            "3Signs" => IconSetValues.ThreeSigns,
            "3Symbols" => IconSetValues.ThreeSymbols,
            "3TrafficLights2" => IconSetValues.ThreeTrafficLights2,
            "4Arrows" => IconSetValues.FourArrows,
            "4Rating" => IconSetValues.FourRating,
            "5Arrows" => IconSetValues.FiveArrows,
            "5Rating" => IconSetValues.FiveRating,
            _ => IconSetValues.ThreeTrafficLights1,
        };
    }
}
