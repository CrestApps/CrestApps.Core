using CrestApps.Core.AI.Documents.Pdf;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;

namespace CrestApps.Core.Tests.Core.Documents.Pdf;

public sealed class PdfDocumentComposerTests
{
    private readonly PdfDocumentComposer _composer = new(Options.Create(new PdfCompositionOptions()), TimeProvider.System);

    [Fact]
    public async Task ComposeAsync_CoverTocAndPageNumbers_ProducesExpectedPages()
    {
        var definition = new PdfDocumentDefinition
        {
            Title = "Quarterly Report",
            Author = "Contoso Analytics",
            CoverPage = new PdfCoverPageDefinition { Enabled = true, Subtitle = "Q3 results", Date = "September 2026" },
            TableOfContents = new PdfTableOfContentsDefinition { Enabled = true },
            PageNumbers = new PdfPageNumbersDefinition { Enabled = true, Template = "Page {page} of {pages}" },
            Sections =
            [
                new PdfSectionDefinition
                {
                    Id = "s1",
                    Blocks =
                    [
                        new PdfBlockDefinition { Id = "b1", Type = "heading", Text = "Summary", Level = 1 },
                        new PdfBlockDefinition { Id = "b2", Type = "paragraph", Text = "Revenue rose by **12%** against plan." },
                        new PdfBlockDefinition { Id = "b3", Type = "heading", Text = "Outlook", Level = 1 },
                        new PdfBlockDefinition { Id = "b4", Type = "list", Items = ["North grew", "South held flat"] },
                    ],
                },
            ],
        };

        var result = await _composer.ComposeAsync(definition, null, TestContext.Current.CancellationToken);

        using var pdf = PdfDocument.Open(result.Bytes);

        Assert.Equal(3, pdf.NumberOfPages);
        Assert.Equal(3, result.PageCount);
        Assert.Contains("Quarterly Report", PageText(pdf.GetPage(1)), StringComparison.Ordinal);
        Assert.Contains("Contents", PageText(pdf.GetPage(2)), StringComparison.Ordinal);
        Assert.Contains("Summary", PageText(pdf.GetPage(2)), StringComparison.Ordinal);

        var body = PageText(pdf.GetPage(3));

        Assert.Contains("12%", body, StringComparison.Ordinal);
        Assert.DoesNotContain("**", body, StringComparison.Ordinal);

        // Pages are numbered as they stand in the file; the cover shows no number but is counted.
        Assert.Contains("Page 3 of 3", body, StringComparison.Ordinal);
        Assert.Contains("Page 2 of 3", PageText(pdf.GetPage(2)), StringComparison.Ordinal);
        Assert.DoesNotContain("Page", PageText(pdf.GetPage(1)), StringComparison.Ordinal);
        Assert.Equal("Quarterly Report", pdf.Information.Title);
    }

    [Fact]
    public async Task ComposeAsync_LandscapeSection_IsWiderThanTall()
    {
        var definition = new PdfDocumentDefinition
        {
            PageSetup = new PdfPageSetupDefinition { Size = "Letter" },
            Sections =
            [
                new PdfSectionDefinition { Id = "s1", Blocks = [new PdfBlockDefinition { Id = "b1", Text = "Portrait page" }] },
                new PdfSectionDefinition
                {
                    Id = "s2",
                    PageSetup = new PdfPageSetupDefinition { Orientation = "landscape" },
                    Blocks = [new PdfBlockDefinition { Id = "b2", Text = "Landscape page" }],
                },
            ],
        };

        var result = await _composer.ComposeAsync(definition, null, TestContext.Current.CancellationToken);

        using var pdf = PdfDocument.Open(result.Bytes);

        Assert.Equal(2, pdf.NumberOfPages);
        Assert.Equal(612, pdf.GetPage(1).Width, 0);
        Assert.Equal(792, pdf.GetPage(1).Height, 0);
        Assert.Equal(792, pdf.GetPage(2).Width, 0);
        Assert.Equal(612, pdf.GetPage(2).Height, 0);
    }

    [Fact]
    public async Task ComposeAsync_FormattedTable_PresentsValuesAndComputesTotals()
    {
        var definition = new PdfDocumentDefinition
        {
            Sections =
            [
                new PdfSectionDefinition
                {
                    Id = "s1",
                    Blocks =
                    [
                        new PdfBlockDefinition
                        {
                            Id = "b1",
                            Type = "table",
                            Table = new PdfTableDefinition
                            {
                                Columns =
                                [
                                    new PdfTableColumnDefinition { Header = "Region" },
                                    new PdfTableColumnDefinition { Header = "Revenue", Format = "currency" },
                                    new PdfTableColumnDefinition { Header = "Share", Format = "percent", Decimals = 1 },
                                ],
                                Rows =
                                [
                                    ["North", "74612.16", "0.25"],
                                    ["South", "1200", "0.75"],
                                ],
                                TotalRow = new PdfTotalRowDefinition
                                {
                                    Functions = new Dictionary<string, string> { ["Revenue"] = "sum", ["Share"] = "sum" },
                                },
                            },
                        },
                    ],
                },
            ],
        };

        var result = await _composer.ComposeAsync(definition, null, TestContext.Current.CancellationToken);

        using var pdf = PdfDocument.Open(result.Bytes);
        var text = string.Join(' ', pdf.GetPage(1).GetWords().Select(word => word.Text));

        Assert.Contains("$74,612.16", text, StringComparison.Ordinal);
        Assert.Contains("25.0%", text, StringComparison.Ordinal);
        Assert.Contains("$75,812.16", text, StringComparison.Ordinal);
        Assert.Contains("100.0%", text, StringComparison.Ordinal);
        Assert.Contains("Total", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComposeAsync_ChartAndUnknownFont_RendersWithWarning()
    {
        var definition = new PdfDocumentDefinition
        {
            Theme = new PdfThemeDefinition { FontFamily = "Imaginary Sans", PrimaryColor = "#0B5394" },
            Sections =
            [
                new PdfSectionDefinition
                {
                    Id = "s1",
                    Blocks =
                    [
                        new PdfBlockDefinition
                        {
                            Id = "b1",
                            Type = "chart",
                            Chart = new PdfChartDefinition
                            {
                                ChartType = "column",
                                Title = "Revenue by region",
                                Labels = ["North", "South"],
                                Series = [new PdfChartSeriesDefinition { Name = "Revenue", Values = [10, 20] }],
                            },
                        },
                    ],
                },
            ],
        };

        var result = await _composer.ComposeAsync(definition, null, TestContext.Current.CancellationToken);

        using var pdf = PdfDocument.Open(result.Bytes);
        var page = pdf.GetPage(1);

        Assert.Contains("Revenue by region", PageText(page), StringComparison.Ordinal);
        Assert.NotEmpty(page.Paths);
        Assert.Contains(result.Warnings, warning => warning.Contains("Imaginary Sans", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ComposeAsync_ImageFromSource_IsPlaced()
    {
        var definition = new PdfDocumentDefinition
        {
            Sections =
            [
                new PdfSectionDefinition
                {
                    Id = "s1",
                    Blocks =
                    [
                        new PdfBlockDefinition { Id = "b1", Type = "image", Image = new PdfImageDefinition { Source = "logo.png", Caption = "Our logo" } },
                        new PdfBlockDefinition { Id = "b2", Type = "image", Image = new PdfImageDefinition { Source = "missing.png" } },
                    ],
                },
            ],
        };

        var source = new DictionaryImageSource(new Dictionary<string, PdfImageData>
        {
            ["logo.png"] = new PdfImageData(PdfTestImages.RedSquarePng(), "image/png", "logo.png"),
        });

        var result = await _composer.ComposeAsync(definition, source, TestContext.Current.CancellationToken);

        using var pdf = PdfDocument.Open(result.Bytes);
        var page = pdf.GetPage(1);

        Assert.Single(page.GetImages());
        Assert.Contains("Our logo", PageText(page), StringComparison.Ordinal);
        Assert.Contains(result.Warnings, warning => warning.Contains("missing.png", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ComposeAsync_Watermark_IsDrawnOnEveryPage()
    {
        var definition = new PdfDocumentDefinition
        {
            Watermark = new PdfWatermarkDefinition { Text = "DRAFT" },
            Sections =
            [
                new PdfSectionDefinition
                {
                    Id = "s1",
                    Blocks =
                    [
                        new PdfBlockDefinition { Id = "b1", Text = "First page" },
                        new PdfBlockDefinition { Id = "b2", Type = "page_break" },
                        new PdfBlockDefinition { Id = "b3", Text = "Second page" },
                    ],
                },
            ],
        };

        var result = await _composer.ComposeAsync(definition, null, TestContext.Current.CancellationToken);

        using var pdf = PdfDocument.Open(result.Bytes);

        Assert.Equal(2, pdf.NumberOfPages);

        foreach (var page in pdf.GetPages())
        {
            // The watermark is drawn on the diagonal, which word extraction splits letter by letter.
            Assert.Contains("DRAFT", string.Concat(page.Letters.Select(letter => letter.Value)), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FormulaEvaluator_EvaluatesArithmeticAndFunctions()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Actual"] = "120",
            ["Planned"] = "100",
            ["Zero"] = "0",
        };

        string Resolve(string name)
        {
            return row.TryGetValue(name, out var value) ? value : null;
        }

        Assert.Equal("20", PdfFormulaEvaluator.Evaluate("={Actual}-{Planned}", Resolve, 2));
        Assert.Equal("0.2", PdfFormulaEvaluator.Evaluate("=({Actual}-{Planned})/{Planned}", Resolve, 2));
        Assert.Equal("#DIV/0!", PdfFormulaEvaluator.Evaluate("={Actual}/{Zero}", Resolve, 2));
        Assert.Equal("n/a", PdfFormulaEvaluator.Evaluate("=IFERROR({Actual}/{Zero},\"n/a\")", Resolve, 2));
        Assert.Equal("Over", PdfFormulaEvaluator.Evaluate("=IF({Actual}>{Planned},\"Over\",\"Under\")", Resolve, 2));
        Assert.Equal("1.33", PdfFormulaEvaluator.Evaluate("=ROUND(4/3,2)", Resolve, 2));
        Assert.Equal("#REF!", PdfFormulaEvaluator.Evaluate("={Missing}+1", Resolve, 2));
        Assert.Equal("3", PdfFormulaEvaluator.Evaluate("={row}+1", Resolve, 2));
    }

    [Fact]
    public void ChartJsReader_ReadsGenerateChartMarker()
    {
        const string Marker = "[chart:{\"type\":\"bar\",\"data\":{\"labels\":[\"A\",\"B\"],\"datasets\":[{\"label\":\"Sales\",\"data\":[1,2],\"backgroundColor\":\"rgba(31,78,121,0.7)\"}]},\"options\":{\"indexAxis\":\"y\",\"plugins\":{\"title\":{\"text\":\"Sales\"}}}}]";

        Assert.True(PdfChartJsReader.TryRead(Marker, out var chart));
        // indexAxis "y" is Chart.js's sideways bar chart; a plain "bar" would be drawn upright.
        Assert.Equal("horizontal_bar", chart.ChartType);
        Assert.Equal("Sales", chart.Title);
        Assert.Equal(["A", "B"], chart.Labels);
        Assert.Equal([1d, 2d], chart.Series[0].Values.Select(value => value.Value));
        Assert.True(PdfColor.TryParse(chart.Series[0].Color, out _));
    }

    private static string PageText(UglyToad.PdfPig.Content.Page page)
    {
        // MigraDoc positions each word itself and writes no space glyphs, so the page's raw text runs words
        // together; the words are what a reader sees.
        return string.Join(' ', page.GetWords().Select(word => word.Text));
    }

    private sealed class DictionaryImageSource : IPdfImageSource
    {
        private readonly Dictionary<string, PdfImageData> _images;

        public DictionaryImageSource(Dictionary<string, PdfImageData> images)
        {
            _images = images;
        }

        public Task<PdfImageData> ResolveAsync(string source, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_images.TryGetValue(source, out var image) ? image : null);
        }
    }
}
