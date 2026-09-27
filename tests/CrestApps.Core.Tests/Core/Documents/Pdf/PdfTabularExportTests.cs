using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.Generation.Spreadsheets;
using CrestApps.Core.AI.Documents.Pdf;
using CrestApps.Core.AI.Documents.Pdf.Rendering;
using CrestApps.Core.AI.Documents.Pdf.Services;
using UglyToad.PdfPig;

namespace CrestApps.Core.Tests.Core.Documents.Pdf;

/// <summary>
/// Verifies that a tabular export written as PDF keeps what the tabular preview shows: number formats,
/// the header colour, the total row and highlighted cells.
/// </summary>
public sealed class PdfTabularExportTests
{
    [Fact]
    public async Task WriteAsync_FormattedSheet_KeepsFormatsColoursAndTotals()
    {
        var content = new GeneratedFileContent
        {
            Title = "Regional revenue",
            Header = ["Region", "Revenue", "Share"],
            Rows =
            [
                ["North", "1200", "0.375"],
                ["South", "2000", "0.625"],
            ],
            SpreadsheetFormatting = new SpreadsheetFormatting
            {
                HeaderStyle = new SpreadsheetCellStyle { BackgroundColor = "#0B5394", FontColor = "#FFFFFF" },
                Columns =
                [
                    new SpreadsheetColumnFormat { Column = "Revenue", NumberFormat = SpreadsheetNumberFormat.Currency, Decimals = 0, CurrencySymbol = "$" },
                    new SpreadsheetColumnFormat { Column = "Share", NumberFormat = SpreadsheetNumberFormat.Percent, Decimals = 1 },
                ],
                TotalRow = new SpreadsheetTotalRow
                {
                    Label = "Total",
                    Columns = [new SpreadsheetTotalColumn { Column = "Revenue", Function = SpreadsheetAggregateFunction.Sum }],
                },
                ConditionalFormats =
                [
                    new SpreadsheetConditionalFormat
                    {
                        Column = "Revenue",
                        Rule = SpreadsheetConditionalRule.GreaterThan,
                        Value = "1500",
                        Style = new SpreadsheetCellStyle { BackgroundColor = "#C6EFCE" },
                    },
                ],
            },
        };

        await using var buffer = new MemoryStream();

        await new PdfGeneratedFileWriter().WriteAsync(content, buffer, TestContext.Current.CancellationToken);

        var bytes = buffer.ToArray();

        using (var pdf = PdfDocument.Open(bytes))
        {
            var text = string.Join(' ', pdf.GetPages().SelectMany(page => page.GetWords()).Select(word => word.Text));

            Assert.Contains("Regional", text, StringComparison.Ordinal);
            Assert.Contains("$1,200", text, StringComparison.Ordinal);
            Assert.Contains("37.5%", text, StringComparison.Ordinal);
            Assert.Contains("Total", text, StringComparison.Ordinal);
            Assert.Contains("$3,200", text, StringComparison.Ordinal);
        }

        // The page is drawn the way a preview draws it; its fills carry the header and highlight colours.
        var svg = Assert.Single(PdfPageSvgRenderer.Render(bytes, [1], new PdfPreviewOptions())).Svg;

        Assert.Contains("#0B5394", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#C6EFCE", svg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteAsync_WideSheet_IsLaidOutInLandscape()
    {
        var header = Enumerable.Range(1, 10).Select(index => "Column " + index).ToList();
        var content = new GeneratedFileContent
        {
            Header = header,
            Rows = [[.. header.Select((_, index) => (index * 10).ToString(System.Globalization.CultureInfo.InvariantCulture))]],
        };

        await using var buffer = new MemoryStream();

        await new PdfGeneratedFileWriter().WriteAsync(content, buffer, TestContext.Current.CancellationToken);

        using var pdf = PdfDocument.Open(buffer.ToArray());
        var page = pdf.GetPage(1);

        Assert.True(page.Width > page.Height);
        Assert.Contains("90", page.GetWords().Select(word => word.Text));
    }
}
