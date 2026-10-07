using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Generation.Spreadsheets;
using CrestApps.Core.AI.Documents.OpenXml.Services;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.Tests.Helpers.DocumentReaders;

public sealed class WordGeneratedFileWriterTests
{
    private readonly WordGeneratedFileWriter _writer = new();

    /// <summary>
    /// The same defect the PDF writer had: an HTML answer was written one literal line per paragraph,
    /// putting raw tags in front of the reader.
    /// </summary>
    [Fact]
    public async Task WriteAsync_HtmlBody_RendersNoMarkup()
    {
        var text = await RenderTextAsync(new GeneratedFileContent
        {
            Title = "Quarterly Report",
            Text =
                """
                <html><head><style>body{font-family:sans-serif}</style></head>
                <body>
                  <h1>Summary</h1>
                  <p>Revenue rose by <strong>12%</strong> against plan.</p>
                  <ul><li>North grew</li><li>South held flat</li></ul>
                </body></html>
                """,
        });

        Assert.DoesNotContain("<", text, StringComparison.Ordinal);
        Assert.DoesNotContain(">", text, StringComparison.Ordinal);
        Assert.DoesNotContain("font-family", text, StringComparison.Ordinal);

        Assert.Contains("Summary", text, StringComparison.Ordinal);
        Assert.Contains("12%", text, StringComparison.Ordinal);
        Assert.Contains("North grew", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that Markdown markers are rendered rather than shown.
    /// </summary>
    [Fact]
    public async Task WriteAsync_MarkdownBody_RendersNoMarkers()
    {
        var text = await RenderTextAsync(new GeneratedFileContent
        {
            Text =
                """
                # Overview

                Revenue rose by **12%** against *plan*.

                - North grew
                - South held flat
                """,
        });

        Assert.DoesNotContain("**", text, StringComparison.Ordinal);
        Assert.DoesNotContain("# ", text, StringComparison.Ordinal);
        Assert.Contains("Overview", text, StringComparison.Ordinal);
        Assert.Contains("12%", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that a Markdown table becomes a real table rather than rows of pipes.
    /// </summary>
    [Fact]
    public async Task WriteAsync_MarkdownTable_BecomesTable()
    {
        using var document = await WriteAsync(new GeneratedFileContent
        {
            Text =
                """
                | Region | Amount |
                | --- | --- |
                | North | 1,000 |
                | South | 2,000 |
                """,
        });

        var table = Assert.Single(document.MainDocumentPart.Document.Body.Elements<Table>());

        // One header row plus two data rows.
        Assert.Equal(3, table.Elements<TableRow>().Count());
    }

    /// <summary>
    /// Verifies that emphasis becomes real run formatting.
    /// </summary>
    [Fact]
    public async Task WriteAsync_BoldText_BecomesBoldRun()
    {
        using var document = await WriteAsync(new GeneratedFileContent
        {
            Text = "Revenue rose by **12%** against plan.",
        });

        var runs = document.MainDocumentPart.Document.Body.Descendants<Run>().ToList();
        var bold = runs.Where(run => run.RunProperties?.GetFirstChild<Bold>() is not null).ToList();

        Assert.Contains(bold, run => run.InnerText == "12%");
    }

    /// <summary>
    /// A document using every supported construct must be structurally valid, because an invalid part
    /// makes the word processor refuse the whole file.
    /// </summary>
    [Fact]
    public async Task WriteAsync_FullyFormattedDocument_IsValid()
    {
        using var document = await WriteAsync(new GeneratedFileContent
        {
            Title = "Monthly Review",
            Text =
                """
                # Overview

                Revenue tracked **2.1% below** plan; see [the workbook](https://example.com/w) for detail.

                ## Actions

                1. Re-forecast the named accounts.
                2. Confirm headcount assumptions.

                - A bulleted note
                - ~~A withdrawn note~~

                | Account | Plan | Actual |
                | --- | --- | --- |
                | Account A | 1,000 | 1,200 |

                > Provisional until the close.

                ---

                ```sql
                SELECT account FROM projections
                ```
                """,
            Header = ["Region", "Amount"],
            Rows = [["North", "1000"]],
        });

        var validator = new OpenXmlValidator();
        var errors = validator.Validate(document, TestContext.Current.CancellationToken).ToList();

        Assert.True(
            errors.Count == 0,
            "The generated document is not valid: " + string.Join(
                Environment.NewLine,
                errors.Select(error => $"{error.Path?.XPath}: {error.Description}")));
    }

    /// <summary>
    /// Verifies that plain prose is unchanged, so ordinary answers are unaffected.
    /// </summary>
    [Fact]
    public async Task WriteAsync_PlainProse_IsPreserved()
    {
        const string Sentence = "The projection covers six sites and forty campaigns.";

        Assert.Contains(Sentence, await RenderTextAsync(new GeneratedFileContent { Text = Sentence }), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_SheetValuesWithMarkdownCharacters_WritesThemLiterally()
    {
        using var document = await WriteAsync(new GeneratedFileContent
        {
            Title = @"Report *draft* for C:\Temp",
            Sheets =
            [
                new GeneratedSheet
                {
                    Name = "Q1_*final*",
                    Header = ["__init__", "Pattern"],
                    Rows = [[@"C:\Users\bob", "*.txt"], ["a_b_c", "~~struck~~"]],
                },
                new GeneratedSheet
                {
                    Name = "Second",
                    Header = ["Value"],
                    Rows = [["**not bold**"]],
                },
            ],
        });

        AssertValid(document);

        var body = document.MainDocumentPart.Document.Body;
        var paragraphs = body.Elements<Paragraph>().Select(paragraph => paragraph.InnerText).ToList();

        Assert.Contains(@"Report *draft* for C:\Temp", paragraphs);
        Assert.Contains("Q1_*final*", paragraphs);

        var tables = body.Elements<Table>().ToList();

        Assert.Equal(2, tables.Count);
        Assert.Equal(["__init__", "Pattern"], CellTexts(tables[0], 0));
        Assert.Equal([@"C:\Users\bob", "*.txt"], CellTexts(tables[0], 1));
        Assert.Equal(["a_b_c", "~~struck~~"], CellTexts(tables[0], 2));
        Assert.Equal(["**not bold**"], CellTexts(tables[1], 1));

        // Nothing in the data was read as emphasis.
        Assert.DoesNotContain(
            body.Descendants<Run>(),
            run => run.RunProperties?.Bold is not null || run.RunProperties?.Italic is not null || run.RunProperties?.Strike is not null);
    }

    [Fact]
    public async Task WriteAsync_MarkdownTableWithEscapedCharacters_KeepsThemLiteral()
    {
        using var document = await WriteAsync(new GeneratedFileContent
        {
            Text =
                """
                | Path | Pattern |
                | --- | --- |
                | C:\\Users\\bob | \*.txt and **bold** |
                """,
        });

        AssertValid(document);

        var table = Assert.Single(document.MainDocumentPart.Document.Body.Elements<Table>());

        Assert.Equal([@"C:\Users\bob", "*.txt and bold"], CellTexts(table, 1));
        Assert.Contains(table.Descendants<Run>(), run => run.InnerText == "bold" && run.RunProperties?.Bold is not null);
    }

    [Fact]
    public async Task WriteAsync_NumberedListWithNestedBullets_GivesNestedItemsBulletMarkers()
    {
        using var document = await WriteAsync(new GeneratedFileContent
        {
            Text =
                """
                1. First
                   - Nested bullet
                   - Another bullet
                2. Second
                """,
        });

        AssertValid(document);

        var items = ListItems(document);

        Assert.Equal(NumberFormatValues.Decimal, ListFormat(document, items["First"]));
        Assert.Equal(NumberFormatValues.Bullet, ListFormat(document, items["Nested bullet"]));
        Assert.Equal(NumberFormatValues.Bullet, ListFormat(document, items["Another bullet"]));
        Assert.Equal(NumberFormatValues.Decimal, ListFormat(document, items["Second"]));

        // The numbered items stay one list, so the second item is numbered 2.
        Assert.Equal(NumberId(items["First"]), NumberId(items["Second"]));
        Assert.Equal(1, Level(items["Nested bullet"]));
    }

    [Fact]
    public async Task WriteAsync_BulletsWithNestedNumbers_GivesNestedItemsNumbers()
    {
        using var document = await WriteAsync(new GeneratedFileContent
        {
            Text =
                """
                - Parent
                  1. Step one
                  2. Step two
                - Sibling
                  - Same-kind child
                """,
        });

        AssertValid(document);

        var items = ListItems(document);

        Assert.Equal(NumberFormatValues.Bullet, ListFormat(document, items["Parent"]));
        Assert.NotEqual(NumberFormatValues.Bullet, ListFormat(document, items["Step one"]));
        Assert.Equal(NumberId(items["Step one"]), NumberId(items["Step two"]));
        Assert.Equal(NumberFormatValues.Bullet, ListFormat(document, items["Sibling"]));

        // A child of the same kind continues its parent's list, as before.
        Assert.Equal(NumberId(items["Parent"]), NumberId(items["Same-kind child"]));
    }

    [Fact]
    public async Task WriteAsync_CountTotalRow_CountsNonEmptyCellsAsWholeNumber()
    {
        using var document = await WriteAsync(new GeneratedFileContent
        {
            Header = ["Item", "Amount"],
            Rows = [["a", "10"], ["b", "n/a"], ["c", string.Empty], ["d", "5"]],
            SpreadsheetFormatting = new SpreadsheetFormatting
            {
                Columns = [new SpreadsheetColumnFormat { Column = "Amount", NumberFormat = SpreadsheetNumberFormat.Currency, Decimals = 2 }],
                TotalRow = new SpreadsheetTotalRow
                {
                    Columns = [new SpreadsheetTotalColumn { Column = "Amount", Function = SpreadsheetAggregateFunction.Count }],
                },
            },
        });

        AssertValid(document);

        var table = Assert.Single(document.MainDocumentPart.Document.Body.Elements<Table>());
        var rows = table.Elements<TableRow>().ToList();

        Assert.Equal(["Total", "3"], CellTexts(table, rows.Count - 1));
    }

    [Theory]
    [InlineData("FFF2CC", false)]
    [InlineData("1F3864", true)]
    public async Task WriteAsync_HeaderFillWithoutFontColor_PicksReadableHeaderText(string fill, bool expectWhite)
    {
        using var document = await WriteAsync(new GeneratedFileContent
        {
            Header = ["Region"],
            Rows = [["North"]],
            SpreadsheetFormatting = new SpreadsheetFormatting
            {
                HeaderStyle = new SpreadsheetCellStyle { BackgroundColor = fill },
            },
        });

        AssertValid(document);

        var table = Assert.Single(document.MainDocumentPart.Document.Body.Elements<Table>());
        var headerRun = table.Elements<TableRow>().First().Descendants<Run>().Single();
        var color = headerRun.RunProperties?.Color?.Val?.Value;

        Assert.NotNull(color);
        Assert.Equal(expectWhite, string.Equals(color, "FFFFFF", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WriteAsync_SomeColumnWidthsSet_GivesUnsetColumnsTheAverageWidth()
    {
        using var document = await WriteAsync(new GeneratedFileContent
        {
            Header = ["Name", "Region", "Notes"],
            Rows = [["a", "b", "c"]],
            SpreadsheetFormatting = new SpreadsheetFormatting
            {
                Columns = [new SpreadsheetColumnFormat { Column = "Name", Width = 30 }],
            },
        });

        var table = Assert.Single(document.MainDocumentPart.Document.Body.Elements<Table>());
        var widths = table.GetFirstChild<TableGrid>().Elements<GridColumn>().Select(column => int.Parse(column.Width.Value, CultureInfo.InvariantCulture)).ToList();

        // Only one width is given, so every column is as wide as it; before, the unset columns were 1/30th of it.
        Assert.Equal(3, widths.Count);
        Assert.InRange(widths[1], widths[0] - 2, widths[0] + 2);
        Assert.InRange(widths[2], widths[0] - 2, widths[0] + 2);
    }

    [Fact]
    public void CreateTable_CellSpanningColumnsAndRows_ContinuesWithSameGridSpan()
    {
        using var package = WordPackage.Create(new WordDesign());

        var spec = new WordTableSpec
        {
            Columns = [new WordTableColumn { Header = "A" }, new WordTableColumn { Header = "B" }, new WordTableColumn { Header = "C" }],
            Rows =
            [
                [new WordTableCell { Text = "Merged", ColumnSpan = 2, RowSpan = 2 }, WordTableCell.FromText("c1")],
                [WordTableCell.FromText("c2")],
                [WordTableCell.FromText("a3"), WordTableCell.FromText("b3"), WordTableCell.FromText("c3")],
            ],
        };

        var table = WordTableWriter.Create(package.MainPart, package.MainPart, spec, new WordDesign(), 9360);

        package.Body.GetFirstChild<SectionProperties>().InsertBeforeSelf(table);

        var rows = table.Elements<TableRow>().ToList();
        var continuation = rows[2].Elements<TableCell>().ToList();

        Assert.Equal(2, continuation.Count);
        Assert.Equal(2, continuation[0].TableCellProperties.GridSpan?.Val?.Value);
        Assert.NotNull(continuation[0].TableCellProperties.VerticalMerge);
        Assert.Equal("c2", continuation[1].InnerText);
        Assert.Equal(3, rows[3].Elements<TableCell>().Count());

        using var saved = WordprocessingDocument.Open(new MemoryStream(package.Save()), isEditable: false);

        AssertValid(saved);
    }

    [Fact]
    public async Task WriteAsync_ManyCodeSpans_AddsInlineCodeStyleOnce()
    {
        var text = string.Join(Environment.NewLine + Environment.NewLine, Enumerable.Range(0, 200).Select(index => $"Call `method{index}` and see [docs](https://example.com/{index})."));

        using var document = await WriteAsync(new GeneratedFileContent { Text = text });

        AssertValid(document);

        var styles = document.MainDocumentPart.StyleDefinitionsPart.Styles.Elements<Style>().ToList();

        Assert.Single(styles, style => style.StyleId?.Value == "InlineCode");
        Assert.Single(styles, style => style.StyleId?.Value == "Hyperlink");
        Assert.Equal(200, document.MainDocumentPart.Document.Body.Descendants<RunStyle>().Count(style => style.Val?.Value == "InlineCode"));
    }

    [Fact]
    public async Task WriteAsync_TextWithControlCharacters_StripsThemAndKeepsLineBreaks()
    {
        using var document = await WriteAsync(new GeneratedFileContent
        {
            Title = "Title\u0001 with bell\u0007",
            Text = "Body\u0000 text\u000B here.",
            Header = ["Notes"],
            Rows = [["first\rsecond\r\nthird\u001F"]],
        });

        AssertValid(document);

        var body = document.MainDocumentPart.Document.Body;

        Assert.Contains("Title with bell", body.Elements<Paragraph>().Select(paragraph => paragraph.InnerText));
        Assert.Contains("Body text here.", body.Elements<Paragraph>().Select(paragraph => paragraph.InnerText));

        var cell = Assert.Single(body.Elements<Table>()).Elements<TableRow>().Last().Elements<TableCell>().Single();

        Assert.Equal("firstsecondthird", cell.InnerText);
        Assert.Equal(2, cell.Descendants<Break>().Count());
    }

    private static List<string> CellTexts(Table table, int rowIndex)
    {
        return [.. table.Elements<TableRow>().ElementAt(rowIndex).Elements<TableCell>().Select(cell => cell.InnerText)];
    }

    private static Dictionary<string, Paragraph> ListItems(WordprocessingDocument document)
    {
        return document.MainDocumentPart.Document.Body.Elements<Paragraph>()
            .Where(paragraph => paragraph.ParagraphProperties?.NumberingProperties is not null)
            .ToDictionary(paragraph => paragraph.InnerText, StringComparer.Ordinal);
    }

    private static int NumberId(Paragraph paragraph)
    {
        return paragraph.ParagraphProperties.NumberingProperties.NumberingId.Val.Value;
    }

    private static int Level(Paragraph paragraph)
    {
        return paragraph.ParagraphProperties.NumberingProperties.NumberingLevelReference.Val.Value;
    }

    private static NumberFormatValues ListFormat(WordprocessingDocument document, Paragraph paragraph)
    {
        var numbering = document.MainDocumentPart.NumberingDefinitionsPart.Numbering;
        var instance = numbering.Elements<NumberingInstance>().Single(item => item.NumberID.Value == NumberId(paragraph));
        var definition = numbering.Elements<AbstractNum>().Single(item => item.AbstractNumberId.Value == instance.AbstractNumId.Val.Value);
        var level = definition.Elements<Level>().Single(item => item.LevelIndex.Value == Level(paragraph));

        return level.NumberingFormat.Val.Value;
    }

    private static void AssertValid(WordprocessingDocument document)
    {
        var errors = new OpenXmlValidator(FileFormatVersions.Microsoft365).Validate(document, TestContext.Current.CancellationToken).ToList();

        Assert.True(
            errors.Count == 0,
            "The generated document is not valid: " + string.Join(
                Environment.NewLine,
                errors.Take(20).Select(error => $"{error.Path?.XPath}: {error.Description}")));
    }

    private async Task<WordprocessingDocument> WriteAsync(GeneratedFileContent content)
    {
        var buffer = new MemoryStream();

        await _writer.WriteAsync(content, buffer, TestContext.Current.CancellationToken);

        buffer.Position = 0;

        return WordprocessingDocument.Open(buffer, isEditable: false);
    }

    private async Task<string> RenderTextAsync(GeneratedFileContent content)
    {
        using var document = await WriteAsync(content);
        var builder = new StringBuilder();

        foreach (var paragraph in document.MainDocumentPart.Document.Body.Descendants<Paragraph>())
        {
            builder.AppendLine(paragraph.InnerText);
        }

        return builder.ToString();
    }
}
