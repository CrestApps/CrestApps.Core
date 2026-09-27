using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Tools;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Intelligence;

public sealed class PdfVisionToolTests
{
    [Fact]
    public async Task Ocr_WithoutVisionModel_SaysSoAndNamesThePagesThatNeedIt()
    {
        using var host = PdfFixtures.Host();
        await host.UploadAsync("scan.pdf", await ScanAsync(2));

        var result = await host.InvokeAsync(new OcrPdfTool());

        Assert.Contains("No vision-capable AI model is configured", result, StringComparison.Ordinal);
        Assert.Contains("Pages that need OCR: 1-2", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ocr_TextPdf_HasNothingToRead()
    {
        using var host = PdfFixtures.Host(vision: new FakeChatClient("unused"));
        await host.UploadAsync("letter.pdf", await PdfFixtures.TextAsync(["This page already has a text layer that can be extracted."]));

        var result = await host.InvokeAsync(new OcrPdfTool());

        Assert.Contains("nothing to OCR", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ocr_ReadsScannedPagesAndMakesThemSearchable()
    {
        var vision = new FakeChatClient(request => request.User.Contains("page 1 ", StringComparison.Ordinal)
            ? "INVOICE 4471\n\nTotal due: $1,250.00"
            : "## Line items\n| Item | Qty |\n|---|---|\n| Widget | 3 |");

        using var host = PdfFixtures.Host(vision: vision);
        await host.UploadAsync("scan.pdf", await ScanAsync(2));

        var result = await host.InvokeAsync(new OcrPdfTool(), new { make_searchable = true });

        Assert.Contains("## Page 1\nINVOICE 4471", result, StringComparison.Ordinal);
        Assert.Contains("| Widget | 3 |", result, StringComparison.Ordinal);
        Assert.Contains("Saved working PDF \"scan\"", result, StringComparison.Ordinal);
        Assert.Contains("approximately", result, StringComparison.Ordinal);
        Assert.Equal(2, vision.Requests.Count);
        Assert.All(vision.Requests, request => Assert.Equal("image/png", Assert.Single(request.Images).MediaType));

        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("scan"));
        var first = pdf.GetPage(1);
        var second = pdf.GetPage(2);

        Assert.Contains("INVOICE 4471", PdfPageText.GetPlainText(first), StringComparison.Ordinal);
        Assert.Contains("$1,250.00", PdfPageText.GetPlainText(first), StringComparison.Ordinal);
        Assert.Contains("Widget 3", PdfPageText.GetPlainText(second), StringComparison.Ordinal);
        Assert.DoesNotContain("|", PdfPageText.GetPlainText(second), StringComparison.Ordinal);

        // The text is there to be found, not seen: every glyph is drawn invisibly over the unchanged scan.
        Assert.All(first.Letters.Where(letter => !string.IsNullOrWhiteSpace(letter.Value)), letter => Assert.Equal(TextRenderingMode.Neither, letter.RenderingMode));
        Assert.Single(first.GetImages());
    }

    [Fact]
    public async Task Ocr_ReadsAtMostTheConfiguredPagesAndSaysWhichRemain()
    {
        var vision = new FakeChatClient("Scanned text");

        using var host = PdfFixtures.Host(vision: vision, configure: options => options.MaxVisionPagesPerCall = 2);
        await host.UploadAsync("scan.pdf", await ScanAsync(3));

        var result = await host.InvokeAsync(new OcrPdfTool());

        Assert.Equal(2, vision.Requests.Count);
        Assert.Contains("pages 3. Call ocr_pdf again with pages \"3\"", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeImages_WithoutVisionModel_ListsTheImages()
    {
        using var host = PdfFixtures.Host();
        await host.UploadAsync("report.pdf", await PdfFixtures.FigureAsync("Quarterly revenue by region.", PdfTestImages.SolidPng(300, 200, 30, 90, 200), "Figure 1: Revenue by region"));

        var result = await host.InvokeAsync(new AnalyzePdfImagesTool());

        Assert.Contains("No vision-capable AI model is configured", result, StringComparison.Ordinal);
        Assert.Contains("## Image 1 — page 1", result, StringComparison.Ordinal);
        Assert.Contains("300×200 px", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeImages_DescribesEachImageWithItsContextAndTheQuestion()
    {
        var vision = new FakeChatClient("Bar chart \"Revenue by region\": North 120, South 80.");

        using var host = PdfFixtures.Host(vision: vision);
        await host.UploadAsync("report.pdf", await PdfFixtures.FigureAsync("Quarterly revenue by region.", PdfTestImages.SolidPng(300, 200, 30, 90, 200), "Figure 1: Revenue by region"));

        var result = await host.InvokeAsync(new AnalyzePdfImagesTool(), new { question = "What values does the chart show?" });

        Assert.Contains("## Image 1 — page 1 (x=", result, StringComparison.Ordinal);
        Assert.Contains("North 120, South 80", result, StringComparison.Ordinal);

        var request = Assert.Single(vision.Requests);

        Assert.Single(request.Images);
        Assert.Contains("Question: What values does the chart show?", request.User, StringComparison.Ordinal);
        Assert.Contains("Revenue by region", request.User, StringComparison.Ordinal);
        Assert.Contains("verbatim", request.System, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeImages_ImageNumbers_CountAcrossTheDocument()
    {
        var vision = new FakeChatClient("A plain grey page.");

        using var host = PdfFixtures.Host(vision: vision);
        await host.UploadAsync("scan.pdf", await ScanAsync(3));

        var result = await host.InvokeAsync(new AnalyzePdfImagesTool(), new { images = new[] { 2 } });

        Assert.Contains("## Image 2 — page 2", result, StringComparison.Ordinal);
        Assert.DoesNotContain("## Image 1", result, StringComparison.Ordinal);
        Assert.Single(vision.Requests);

        var missing = await host.InvokeAsync(new AnalyzePdfImagesTool(), new { images = new[] { 9 } });

        Assert.Contains("only 3 image(s)", missing, StringComparison.Ordinal);
    }

    private static Task<byte[]> ScanAsync(int pages)
    {
        return PdfFixtures.ScannedAsync([.. Enumerable.Range(0, pages).Select(index => PdfTestImages.SolidPng(120, 160, (byte)(200 + index), 200, 200))]);
    }
}
