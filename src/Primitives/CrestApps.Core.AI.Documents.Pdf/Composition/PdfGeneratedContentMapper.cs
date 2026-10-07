using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Generation.Spreadsheets;

namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// Describes <see cref="GeneratedFileContent"/> — the format-agnostic content <c>generate_file</c> and the
/// tabular export hand every writer — as a composed PDF.
/// </summary>
/// <remarks>
/// A tabular export's presentation arrives as <see cref="SpreadsheetFormatting"/>, written for a workbook.
/// The PDF has to honour the same description — the number formats, the header colours, the banding, the
/// highlighted cells, the calculated columns, the total row and the charts — or the report reads differently
/// depending on which format the reader asked for. The column plan comes from <see cref="SpreadsheetLayout"/>,
/// the same resolution the workbook writer uses, so calculated columns land in the same place and a format
/// inherited from the uploaded file is kept.
/// </remarks>
internal static class PdfGeneratedContentMapper
{
    private const int LandscapeColumnThreshold = 7;

    /// <summary>
    /// Builds the composed document for generated content.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <returns>The document.</returns>
    public static PdfDocumentDefinition ToDefinition(GeneratedFileContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var definition = new PdfDocumentDefinition
        {
            Title = string.IsNullOrWhiteSpace(content.Title) ? null : content.Title.Trim(),
            PageNumbers = new PdfPageNumbersDefinition
            {
                Enabled = true,
                Position = "footer-right",
                Template = "Page {page} of {pages}",
            },
        };

        var section = AddSection(definition, landscape: false);

        if (!string.IsNullOrWhiteSpace(content.Title))
        {
            section.Blocks.Add(NewBlock(definition, new PdfBlockDefinition
            {
                Type = PdfBlockTypes.Heading,
                Level = 1,
                Text = content.Title.Trim(),
                FontSize = 20,
            }));
        }

        if (!string.IsNullOrEmpty(content.Text))
        {
            section.Blocks.Add(NewBlock(definition, new PdfBlockDefinition
            {
                Type = PdfBlockTypes.Markdown,
                Text = content.Text,
            }));
        }

        if (content.HasTable)
        {
            var sheets = content.GetSheets().Where(sheet => sheet.HasTable).ToList();

            for (var sheetIndex = 0; sheetIndex < sheets.Count; sheetIndex++)
            {
                var sheet = sheets[sheetIndex];
                var layout = SpreadsheetLayout.Create(sheet);
                var wide = layout.Columns.Count > LandscapeColumnThreshold;

                // Each sheet starts on a page of its own, and a wide one goes on a landscape page rather than
                // being squeezed into a portrait one until nothing in it is readable.
                if (section.Blocks.Count > 0 && (wide || sheetIndex > 0))
                {
                    section = AddSection(definition, wide);
                }
                else if (wide)
                {
                    section.PageSetup = new PdfPageSetupDefinition { Orientation = "landscape" };
                }

                if (sheets.Count > 1 && !string.IsNullOrWhiteSpace(sheet.Name))
                {
                    section.Blocks.Add(NewBlock(definition, new PdfBlockDefinition
                    {
                        Type = PdfBlockTypes.Heading,
                        Level = 2,
                        Text = sheet.Name,
                    }));
                }

                section.Blocks.Add(NewBlock(definition, new PdfBlockDefinition
                {
                    Type = PdfBlockTypes.Table,
                    Table = ToTable(layout),
                }));

                foreach (var chart in layout.Formatting.Charts ?? [])
                {
                    var mapped = ToChart(chart, layout);

                    if (mapped is not null)
                    {
                        section.Blocks.Add(NewBlock(definition, new PdfBlockDefinition
                        {
                            Type = PdfBlockTypes.Chart,
                            Chart = mapped,
                        }));
                    }
                }
            }
        }

        return definition;
    }

    /// <summary>
    /// Describes a resolved sheet as a table, computing the calculated columns a workbook would compute.
    /// </summary>
    /// <param name="layout">The sheet plan.</param>
    /// <returns>The table.</returns>
    public static PdfTableDefinition ToTable(SpreadsheetLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var formatting = layout.Formatting;
        var styled = formatting.StyleHeader != false;
        var table = new PdfTableDefinition
        {
            Banded = formatting.BandedRows == true,
            BandColor = formatting.BandColor,
            HeaderBackground = styled ? formatting.HeaderStyle?.BackgroundColor ?? PdfResolvedTheme.DefaultPrimary.ToHex() : "#FFFFFF",
            HeaderTextColor = styled ? formatting.HeaderStyle?.FontColor ?? "#FFFFFF" : "#1F2933",
        };

        foreach (var column in layout.Columns)
        {
            table.Columns.Add(new PdfTableColumnDefinition
            {
                Header = column.Name,
                FormatCode = IsPresentableCode(column.NumberFormatCode) ? column.NumberFormatCode : null,
                NegativesInRed = column.Format?.NegativesInRed == true ? true : null,
                Bold = column.Style?.Bold,
                Color = column.Style?.FontColor,
                BackgroundColor = column.Style?.BackgroundColor,
                Align = column.Style?.Alignment switch
                {
                    SpreadsheetHorizontalAlignment.Left => "left",
                    SpreadsheetHorizontalAlignment.Center => "center",
                    SpreadsheetHorizontalAlignment.Right => "right",
                    _ => null,
                },
            });
        }

        for (var rowIndex = 0; rowIndex < layout.Rows.Count; rowIndex++)
        {
            table.Rows.Add(ResolveRow(layout, rowIndex));
        }

        if (formatting.TotalRow is not null)
        {
            table.TotalRow = new PdfTotalRowDefinition
            {
                Label = formatting.TotalRow.Label,
                Functions = (formatting.TotalRow.Columns ?? [])
                    .Where(column => column is not null && !string.IsNullOrWhiteSpace(column.Column))
                    .GroupBy(column => column.Column, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First().Function.ToString().ToLowerInvariant(), StringComparer.OrdinalIgnoreCase),
            };
        }

        var rules = new List<PdfHighlightRuleDefinition>();

        foreach (var conditional in formatting.ConditionalFormats ?? [])
        {
            var rule = ToHighlightRule(conditional);

            if (rule is not null)
            {
                rules.Add(rule);
            }
        }

        table.HighlightRules = rules.Count > 0 ? rules : null;

        return table;
    }

    private static List<string> ResolveRow(SpreadsheetLayout layout, int rowIndex)
    {
        var source = layout.Rows[rowIndex];
        var values = new string[layout.Columns.Count];
        var resolving = new bool[layout.Columns.Count];
        var rowNumber = SpreadsheetLayout.FirstDataRowNumber + rowIndex;

        string Value(int columnIndex)
        {
            if (values[columnIndex] is not null)
            {
                return values[columnIndex];
            }

            var column = layout.Columns[columnIndex];

            if (string.IsNullOrWhiteSpace(column.Formula))
            {
                return values[columnIndex] = column.Index < source.Count ? source[column.Index] ?? string.Empty : string.Empty;
            }

            if (resolving[columnIndex])
            {
                // A formula that reads itself, however indirectly, is the error a spreadsheet reports for it.
                return "#REF!";
            }

            resolving[columnIndex] = true;

            var result = PdfFormulaEvaluator.Evaluate(
                column.Formula,
                name => layout.TryGetColumnIndex(name, out var referenced) ? Value(referenced) : null,
                rowNumber);

            resolving[columnIndex] = false;

            return values[columnIndex] = result;
        }

        var row = new List<string>(layout.Columns.Count);

        for (var index = 0; index < layout.Columns.Count; index++)
        {
            row.Add(Value(index));
        }

        return row;
    }

    private static PdfHighlightRuleDefinition ToHighlightRule(SpreadsheetConditionalFormat conditional)
    {
        if (conditional is null || string.IsNullOrWhiteSpace(conditional.Column))
        {
            return null;
        }

        var style = conditional.Style;
        var rule = new PdfHighlightRuleDefinition
        {
            Column = conditional.Column,
            Value = conditional.Value,
            Value2 = conditional.SecondValue,
            BackgroundColor = style?.BackgroundColor,
            Color = style?.FontColor,
            Bold = style?.Bold,
        };

        switch (conditional.Rule)
        {
            case SpreadsheetConditionalRule.GreaterThan:
                rule.Operator = "gt";

                break;
            case SpreadsheetConditionalRule.LessThan:
                rule.Operator = "lt";

                break;
            case SpreadsheetConditionalRule.EqualTo:
                rule.Operator = "eq";

                break;
            case SpreadsheetConditionalRule.Between:
                rule.Operator = "between";

                break;
            case SpreadsheetConditionalRule.ContainsText:
                rule.Operator = "contains";

                break;
            case SpreadsheetConditionalRule.DuplicateValues:
                rule.Operator = "duplicate";

                break;
            case SpreadsheetConditionalRule.ColorScale:
                rule.Operator = "scale";
                rule.MinimumColor = conditional.MinimumColor;
                rule.BackgroundColor = conditional.MaximumColor;

                break;
            case SpreadsheetConditionalRule.DataBar:
                // A printed page has no data bar; a scale from white to the bar colour carries the same
                // "longer is more" reading.
                rule.Operator = "scale";
                rule.MinimumColor = "#FFFFFF";
                rule.BackgroundColor = conditional.BarColor ?? "#638EC6";

                break;
            default:
                return null;
        }

        return rule;
    }

    private static PdfChartDefinition ToChart(SpreadsheetChart chart, SpreadsheetLayout layout)
    {
        if (chart is null || chart.ValueColumns is not { Count: > 0 } || layout.Rows.Count == 0)
        {
            return null;
        }

        var categoryIndex = !string.IsNullOrWhiteSpace(chart.CategoryColumn) && layout.TryGetColumnIndex(chart.CategoryColumn, out var found)
            ? found
            : 0;

        var limit = Math.Clamp(chart.MaxCategories ?? SpreadsheetFormatting.DefaultMaxChartCategories, 1, 200);
        var rows = new List<List<string>>();

        for (var index = 0; index < layout.Rows.Count && rows.Count < limit; index++)
        {
            rows.Add(ResolveRow(layout, index));
        }

        var definition = new PdfChartDefinition
        {
            ChartType = chart.Kind switch
            {
                // A spreadsheet's bar chart is the sideways one; its column chart is upright.
                SpreadsheetChartKind.Bar => "horizontal_bar",
                SpreadsheetChartKind.Line => "line",
                SpreadsheetChartKind.Pie => "pie",
                SpreadsheetChartKind.Area => "area",
                _ => "column",
            },
            Title = chart.Title,
            Labels = [.. rows.Select(row => categoryIndex < row.Count ? row[categoryIndex] : string.Empty)],
        };

        foreach (var valueColumn in chart.ValueColumns)
        {
            if (!layout.TryGetColumnIndex(valueColumn, out var valueIndex))
            {
                continue;
            }

            var series = new PdfChartSeriesDefinition { Name = layout.Columns[valueIndex].Name };

            foreach (var row in rows)
            {
                series.Values.Add(valueIndex < row.Count && PdfCellValues.TryParseNumber(row[valueIndex], percentAsFraction: false, out var number)
                    ? number
                    : null);
            }

            definition.Series.Add(series);
        }

        return definition.Series.Count == 0 ? null : definition;
    }

    private static bool IsPresentableCode(string code)
    {
        return !string.IsNullOrWhiteSpace(code) &&
            !string.Equals(code, "General", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(code, "@", StringComparison.Ordinal);
    }

    private static PdfSectionDefinition AddSection(PdfDocumentDefinition definition, bool landscape)
    {
        var section = new PdfSectionDefinition
        {
            Id = "s" + definition.NextSectionNumber++,
            PageSetup = landscape ? new PdfPageSetupDefinition { Orientation = "landscape" } : null,
        };

        definition.Sections.Add(section);

        return section;
    }

    private static PdfBlockDefinition NewBlock(PdfDocumentDefinition definition, PdfBlockDefinition block)
    {
        block.Id = "b" + definition.NextBlockNumber++;

        return block;
    }
}
