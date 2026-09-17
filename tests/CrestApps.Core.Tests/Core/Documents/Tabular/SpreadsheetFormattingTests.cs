using System.Text.Json;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Generation.Spreadsheets;

namespace CrestApps.Core.Tests.Core.Documents.Tabular;

public sealed class SpreadsheetFormattingTests
{
    /// <summary>
    /// Verifies that the model-facing shape is read into a specification, including the inline style
    /// members that sit alongside the format members.
    /// </summary>
    [Fact]
    public void Parse_ColumnRequest_ReadsFormatAndStyle()
    {
        var formatting = Parse(
            """
            {
              "columns": [
                {
                  "column": "Amount",
                  "format": "currency",
                  "decimals": 2,
                  "currency_symbol": "EUR",
                  "negatives_in_red": true,
                  "align": "right",
                  "bold": true,
                  "width": 18
                }
              ]
            }
            """);

        var column = Assert.Single(formatting.Columns);

        Assert.Equal("Amount", column.Column);
        Assert.Equal(SpreadsheetNumberFormat.Currency, column.NumberFormat);
        Assert.Equal(2, column.Decimals);
        Assert.Equal("EUR", column.CurrencySymbol);
        Assert.True(column.NegativesInRed);
        Assert.Equal(18, column.Width);
        Assert.Equal(SpreadsheetHorizontalAlignment.Right, column.Style.Alignment);
        Assert.True(column.Style.Bold);
    }

    /// <summary>
    /// Models do not spell enums consistently, so a request must survive alternative wordings rather
    /// than silently falling back to a default the user did not ask for.
    /// </summary>
    [Theory]
    [InlineData("color_scale", SpreadsheetConditionalRule.ColorScale)]
    [InlineData("colorScale", SpreadsheetConditionalRule.ColorScale)]
    [InlineData("Color Scale", SpreadsheetConditionalRule.ColorScale)]
    [InlineData("gradient", SpreadsheetConditionalRule.ColorScale)]
    [InlineData("heatmap", SpreadsheetConditionalRule.ColorScale)]
    [InlineData("less_than", SpreadsheetConditionalRule.LessThan)]
    [InlineData("LESS THAN", SpreadsheetConditionalRule.LessThan)]
    [InlineData("data_bar", SpreadsheetConditionalRule.DataBar)]
    public void Parse_ConditionalRuleSpelling_IsUnderstood(string spelling, SpreadsheetConditionalRule expected)
    {
        var formatting = Parse(
            $$"""
            {
              "conditional_formats": [
                { "column": "Variance", "rule": {{JsonSerializer.Serialize(spelling)}} }
              ]
            }
            """);

        Assert.Equal(expected, Assert.Single(formatting.ConditionalFormats).Rule);
    }

    /// <summary>
    /// Verifies that a numeric threshold supplied as a JSON number rather than a string is still read,
    /// since either is a natural way to express it.
    /// </summary>
    [Fact]
    public void Parse_NumericThreshold_IsRead()
    {
        var formatting = Parse(
            """
            {
              "conditional_formats": [
                { "column": "Amount", "rule": "less_than", "value": 0 }
              ]
            }
            """);

        Assert.Equal("0", Assert.Single(formatting.ConditionalFormats).Value);
    }

    /// <summary>
    /// Verifies that a total row written as bare column names is understood, defaulting to a sum.
    /// </summary>
    [Fact]
    public void Parse_TotalRowWithBareColumnNames_DefaultsToSum()
    {
        var formatting = Parse(
            """
            {
              "total_row": { "label": "Total", "columns": ["Amount", "Units"] }
            }
            """);

        Assert.Equal("Total", formatting.TotalRow.Label);
        Assert.Equal(2, formatting.TotalRow.Columns.Count);
        Assert.All(formatting.TotalRow.Columns, column => Assert.Equal(SpreadsheetAggregateFunction.Sum, column.Function));
    }

    /// <summary>
    /// Verifies that the sheet-level switches stay unset when they are not mentioned, which is what
    /// lets a later request merge without disturbing them.
    /// </summary>
    [Fact]
    public void Parse_UnmentionedSwitches_StayUnset()
    {
        var formatting = Parse("""{ "columns": [{ "column": "Amount", "format": "currency" }] }""");

        Assert.Null(formatting.FreezeHeader);
        Assert.Null(formatting.AutoFilter);
        Assert.Null(formatting.StyleHeader);
        Assert.Null(formatting.BandedRows);
    }

    /// <summary>
    /// Verifies that an unknown member is ignored rather than failing the whole request, so one
    /// misspelled option never costs the user everything else they asked for.
    /// </summary>
    [Fact]
    public void Parse_UnknownMembers_AreIgnored()
    {
        var formatting = Parse(
            """
            {
              "columns": [{ "column": "Amount", "format": "currency", "sparkle": true }],
              "nonsense": { "deeply": ["nested"] }
            }
            """);

        Assert.Equal(SpreadsheetNumberFormat.Currency, Assert.Single(formatting.Columns).NumberFormat);
    }

    /// <summary>
    /// Verifies that a follow-up request keeps the formatting recorded earlier, which is what makes a
    /// conversational "now also ..." work without restating everything.
    /// </summary>
    [Fact]
    public void Merge_FollowUpRequest_KeepsEarlierFormatting()
    {
        var existing = Parse(
            """
            {
              "columns": [{ "column": "Amount", "format": "currency" }],
              "auto_filter": false
            }
            """);

        var request = Parse(
            """
            {
              "conditional_formats": [{ "column": "Amount", "rule": "color_scale" }]
            }
            """);

        var merged = SpreadsheetFormattingMerge.Merge(existing, request);

        Assert.Equal(SpreadsheetNumberFormat.Currency, Assert.Single(merged.Columns).NumberFormat);
        Assert.Equal(SpreadsheetConditionalRule.ColorScale, Assert.Single(merged.ConditionalFormats).Rule);

        // A switch the user turned off earlier must not come back just because a later request did not
        // mention it.
        Assert.False(merged.AutoFilter);
    }

    /// <summary>
    /// Verifies that restating a column replaces its format outright rather than leaving half of the
    /// previous one in place.
    /// </summary>
    [Fact]
    public void Merge_RestatedColumn_ReplacesPreviousFormat()
    {
        var existing = Parse("""{ "columns": [{ "column": "Amount", "format": "currency", "decimals": 2 }] }""");
        var request = Parse("""{ "columns": [{ "column": "Amount", "format": "percent" }] }""");

        var merged = SpreadsheetFormattingMerge.Merge(existing, request);
        var column = Assert.Single(merged.Columns);

        Assert.Equal(SpreadsheetNumberFormat.Percent, column.NumberFormat);
        Assert.Null(column.Decimals);
    }

    /// <summary>
    /// Verifies that two different rules on one column both survive, since they express two separate
    /// requests rather than one overriding the other.
    /// </summary>
    [Fact]
    public void Merge_DifferentRulesOnSameColumn_BothSurvive()
    {
        var existing = Parse("""{ "conditional_formats": [{ "column": "Amount", "rule": "color_scale" }] }""");
        var request = Parse("""{ "conditional_formats": [{ "column": "Amount", "rule": "less_than", "value": "0" }] }""");

        var merged = SpreadsheetFormattingMerge.Merge(existing, request);

        Assert.Equal(2, merged.ConditionalFormats.Count);
    }

    /// <summary>
    /// Verifies that a specification survives the round trip through the workspace's storage, since
    /// formatting recorded in one turn is read back in a later one.
    /// </summary>
    [Fact]
    public void SerializeAndDeserialize_RoundTripsSpecification()
    {
        var formatting = Parse(
            """
            {
              "sheet_name": "Summary",
              "banded_rows": true,
              "columns": [
                { "column": "Amount", "format": "currency", "decimals": 2 },
                { "column": "Variance", "formula": "={Actual}-{Planned}", "format": "number" }
              ],
              "conditional_formats": [{ "column": "Variance", "rule": "color_scale", "min_color": "#F8696B" }],
              "total_row": { "label": "Total", "columns": [{ "column": "Amount", "function": "average" }] },
              "charts": [{ "type": "bar", "title": "By region", "category_column": "Region", "value_columns": ["Amount"] }]
            }
            """);

        var restored = SpreadsheetFormattingJson.Deserialize(SpreadsheetFormattingJson.Serialize(formatting));

        Assert.Equal("Summary", restored.SheetName);
        Assert.True(restored.BandedRows);
        Assert.Equal(2, restored.Columns.Count);
        Assert.Equal("={Actual}-{Planned}", restored.Columns[1].Formula);
        Assert.Equal("#F8696B", Assert.Single(restored.ConditionalFormats).MinimumColor);
        Assert.Equal(SpreadsheetAggregateFunction.Average, Assert.Single(restored.TotalRow.Columns).Function);
        Assert.Equal(SpreadsheetChartKind.Bar, Assert.Single(restored.Charts).Kind);
    }

    /// <summary>
    /// Verifies that unreadable stored formatting yields no specification rather than throwing, so a
    /// corrupt presentation detail never costs the user their data export.
    /// </summary>
    [Fact]
    public void Deserialize_CorruptStoredValue_ReturnsNull()
    {
        Assert.Null(SpreadsheetFormattingJson.Deserialize("{ this is not json"));
    }

    /// <summary>
    /// Verifies the currency format code, since the whole point of the feature is that the reader sees
    /// a currency amount rather than a bare number.
    /// </summary>
    [Fact]
    public void NumberFormatCode_Currency_BuildsExpectedCode()
    {
        var code = SpreadsheetNumberFormatCode.Resolve(new SpreadsheetColumnFormat
        {
            NumberFormat = SpreadsheetNumberFormat.Currency,
            Decimals = 2,
            NegativesInRed = true,
        });

        Assert.Equal("\"$\"#,##0.00_);[Red](\"$\"#,##0.00)", code);
    }

    /// <summary>
    /// Verifies that an explicit format code wins, so a request this library has no named format for
    /// is still honored.
    /// </summary>
    [Fact]
    public void NumberFormatCode_ExplicitCode_Wins()
    {
        var code = SpreadsheetNumberFormatCode.Resolve(new SpreadsheetColumnFormat
        {
            NumberFormat = SpreadsheetNumberFormat.Currency,
            FormatCode = "0.000",
        });

        Assert.Equal("0.000", code);
    }

    /// <summary>
    /// Verifies that column names convert past the single-letter range, which a wide export reaches.
    /// </summary>
    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(27, "AB")]
    [InlineData(51, "AZ")]
    [InlineData(52, "BA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    public void ToColumnName_ConvertsIndex(int index, string expected)
    {
        Assert.Equal(expected, SpreadsheetFormula.ToColumnName(index));
    }

    /// <summary>
    /// Verifies that a formula's column placeholders resolve to the referenced cells on the same row.
    /// </summary>
    [Fact]
    public void ResolveFormula_ColumnPlaceholders_BecomeCellReferences()
    {
        var layout = CreateLayout(["Planned", "Actual"], [["1", "2"]]);

        var formula = SpreadsheetFormula.Resolve("={Actual}-{Planned}", 7, layout.TryGetColumnIndex);

        Assert.Equal("B7-A7", formula);
    }

    /// <summary>
    /// Verifies that an unresolvable placeholder drops the formula, because a broken reference makes
    /// the spreadsheet application reject the entire workbook.
    /// </summary>
    [Fact]
    public void ResolveFormula_UnknownPlaceholder_ReturnsNull()
    {
        var layout = CreateLayout(["Planned"], [["1"]]);

        Assert.Null(SpreadsheetFormula.Resolve("={Missing}*2", 2, layout.TryGetColumnIndex));
    }

    /// <summary>
    /// Verifies that the layout types a clean numeric column as a number, which is the decision the
    /// whole export depends on.
    /// </summary>
    [Fact]
    public void Layout_CleanNumericColumn_IsTypedAsNumber()
    {
        var layout = CreateLayout(["Region", "Amount"], [["North", "1000"], ["South", "2000.50"]]);

        Assert.Equal(SpreadsheetDataKind.Text, layout.Columns[0].Kind);
        Assert.Equal(SpreadsheetDataKind.Number, layout.Columns[1].Kind);
    }

    /// <summary>
    /// Verifies that a worksheet name containing characters the file format reserves is sanitized,
    /// rather than producing a workbook that cannot be opened.
    /// </summary>
    [Fact]
    public void Layout_SheetNameWithReservedCharacters_IsSanitized()
    {
        var content = new GeneratedFileContent
        {
            Header = ["Value"],
            Rows = [["1"]],
            SpreadsheetFormatting = new SpreadsheetFormatting { SheetName = "Q3/Q4 [draft]" },
        };

        var layout = SpreadsheetLayout.Create(content);

        Assert.DoesNotContain("/", layout.SheetName, StringComparison.Ordinal);
        Assert.DoesNotContain("[", layout.SheetName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a worksheet name longer than the format allows is truncated.
    /// </summary>
    [Fact]
    public void Layout_OverlongSheetName_IsTruncated()
    {
        var content = new GeneratedFileContent
        {
            Header = ["Value"],
            Rows = [["1"]],
            SpreadsheetFormatting = new SpreadsheetFormatting { SheetName = new string('x', 80) },
        };

        Assert.Equal(31, SpreadsheetLayout.Create(content).SheetName.Length);
    }

    /// <summary>
    /// A column must be wide enough for the value as it is DISPLAYED, not as it is stored. A currency
    /// format adds a symbol, separators, decimals, and an alignment space, and a column too narrow for
    /// the result renders as <c>######</c> rather than wrapping — which reads as a broken file. This
    /// was found by opening a generated workbook in Excel, not by inspecting the file.
    /// </summary>
    [Fact]
    public void Layout_CurrencyColumn_IsWideEnoughForTheRenderedValue()
    {
        var content = new GeneratedFileContent
        {
            Header = ["Amount"],
            Rows = [["837471.00"], ["801005.25"]],
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

        // "$837,471.00 " is twelve characters; the raw value is only nine.
        Assert.True(
            SpreadsheetLayout.Create(content).Columns[0].Width >= 12,
            "A currency column must fit its rendered value, not its raw value.");
    }

    /// <summary>
    /// Verifies that a calculated column is sized from the columns its formula reads. It has no values
    /// of its own, so measuring it from its header alone leaves it far too narrow.
    /// </summary>
    [Fact]
    public void Layout_ComputedColumn_IsSizedFromItsReferences()
    {
        var content = new GeneratedFileContent
        {
            Header = ["Plan", "Actual"],
            Rows = [["837471.00", "801005.25"]],
            SpreadsheetFormatting = new SpreadsheetFormatting
            {
                Columns =
                [
                    new SpreadsheetColumnFormat
                    {
                        Column = "Var",
                        Formula = "={Actual}-{Plan}",
                        NumberFormat = SpreadsheetNumberFormat.Currency,
                        Decimals = 2,
                    },
                ],
            },
        };

        var computed = SpreadsheetLayout.Create(content).Columns[2];

        Assert.True(computed.IsComputed);
        Assert.True(
            computed.Width >= 12,
            $"A calculated column must be sized from its references, but was {computed.Width}.");
    }

    /// <summary>
    /// Verifies that a summed column fits its total, which is larger than any single row.
    /// </summary>
    [Fact]
    public void Layout_SummedColumn_IsWideEnoughForTheTotal()
    {
        var rows = Enumerable.Range(0, 50)
            .Select(_ => (IReadOnlyList<string>)new List<string> { "837471.00" })
            .ToList();

        var content = new GeneratedFileContent
        {
            Header = ["Amount"],
            Rows = rows,
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
                TotalRow = new SpreadsheetTotalRow
                {
                    Columns = [new SpreadsheetTotalColumn { Column = "Amount", Function = SpreadsheetAggregateFunction.Sum }],
                },
            },
        };

        // The total is around $41,873,550.00, two digits and a separator wider than any single row.
        Assert.True(
            SpreadsheetLayout.Create(content).Columns[0].Width >= 15,
            "A summed column must fit its total, not just its widest row.");
    }

    /// <summary>
    /// A computed value from SQL routinely carries full floating-point precision. Rejecting it for
    /// having too many digits put the number back in the sheet as text — the exact complaint this work
    /// set out to fix. Found by exporting a real joined report and opening it in Excel.
    /// </summary>
    [Theory]
    [InlineData("-3313.2599999999948")]
    [InlineData("0.30000000000000004")]
    [InlineData("1234567890123456.75")]
    public void LooksNumeric_LongDecimal_IsStillANumber(string value)
    {
        Assert.True(SpreadsheetValue.LooksNumeric(value, out var number));
        Assert.NotEqual(0, number);
    }

    /// <summary>
    /// Verifies that the digit ceiling still protects a long whole number, which is an identifier
    /// rather than a quantity and must keep every character.
    /// </summary>
    [Theory]
    [InlineData("12345678901234567890")]
    [InlineData("00123456")]
    public void LooksNumeric_LongWholeNumber_StaysText(string value)
    {
        Assert.False(SpreadsheetValue.LooksNumeric(value, out _));
    }

    /// <summary>
    /// Verifies that a column of full-precision computed values is typed numerically, so the reader can
    /// sum and chart it.
    /// </summary>
    [Fact]
    public void Layout_ComputedPrecisionColumn_IsTypedAsNumber()
    {
        var layout = CreateLayout(
            ["Client", "Variance"],
            [["Atlas", "-3313.2599999999948"], ["Borealis", "4239.070000000003"]]);

        Assert.Equal(SpreadsheetDataKind.Number, layout.Columns[1].Kind);
    }

    /// <summary>
    /// Verifies that a date column fits the format it is given.
    /// </summary>
    [Fact]
    public void Layout_DateColumn_FitsTheFormattedDate()
    {
        var layout = CreateLayout(["Posted"], [["2026-09-15"]]);

        Assert.Equal(SpreadsheetDataKind.Date, layout.Columns[0].Kind);
        Assert.True(layout.Columns[0].Width >= 11);
    }

    /// <summary>
    /// Verifies that a calculated column whose formula names a column the sheet does not have is
    /// reported rather than written. This is the shape that reached a user: a "Variance %" column was
    /// recorded against a comparison report, the export ran without a query and so delivered the two
    /// untouched source tables, and the column arrived as a header with nothing underneath it.
    /// </summary>
    [Fact]
    public void Layout_FormulaReferencingAMissingColumn_IsReported()
    {
        var formatting = Parse(
            """
            {
              "columns": [
                { "column": "Variance %", "formula": "={Variance}/{Site Figure}", "format": "percent" }
              ]
            }
            """);

        var layout = CreateLayout(
            ["Client", "September Projection"],
            [["Atlas Health", "95400"], ["Borealis Bank", "81000"]],
            formatting);

        Assert.Equal(["Variance %"], layout.GetUnresolvableFormulaColumns());
    }

    /// <summary>
    /// Verifies that a formula whose references all exist is not reported, so the check only fires on a
    /// column that would genuinely arrive empty.
    /// </summary>
    [Fact]
    public void Layout_FormulaReferencingPresentColumns_IsNotReported()
    {
        var formatting = Parse(
            """
            {
              "columns": [
                { "column": "Variance", "formula": "={Actual}-{Planned}", "format": "currency" }
              ]
            }
            """);

        var layout = CreateLayout(
            ["Client", "Planned", "Actual"],
            [["Atlas Health", "95400", "92086.74"]],
            formatting);

        Assert.Empty(layout.GetUnresolvableFormulaColumns());
    }

    private static SpreadsheetLayout CreateLayout(
        string[] header,
        List<List<string>> rows,
        SpreadsheetFormatting formatting)
    {
        return SpreadsheetLayout.Create(new GeneratedFileContent
        {
            Header = header,
            Rows = rows.Cast<IReadOnlyList<string>>().ToList(),
            SpreadsheetFormatting = formatting,
        });
    }

    private static SpreadsheetLayout CreateLayout(string[] header, List<List<string>> rows)
    {
        return SpreadsheetLayout.Create(new GeneratedFileContent
        {
            Header = header,
            Rows = rows.Cast<IReadOnlyList<string>>().ToList(),
        });
    }

    private static SpreadsheetFormatting Parse(string json)
    {
        return SpreadsheetFormattingJson.Parse(JsonDocument.Parse(json).RootElement);
    }
}
