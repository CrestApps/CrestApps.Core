using CrestApps.Core.AI.Documents.Pdf.Tools;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace CrestApps.Core.Tests.Core.Documents.Pdf;

public sealed class EditPdfContentToolTests
{
    [Fact]
    public async Task ReplaceText_RemovesTheOldGlyphsAndSetsTheNewTextOnTheSameBaseline()
    {
        using var host = new PdfToolTestHost();
        var original = PdfToolFixtures.StandardFontPdf("BT /F1 14 Tf 1 0 0 rg 72 700 Td (Contoso budget for 2025 approved.) Tj ET");
        await host.UploadAsync("budget.pdf", original);

        var result = await host.InvokeAsync(new EditPdfContentTool(), new
        {
            operations = new[] { new { operation = "replace_text", find = "2025", replace = "2026" } },
        });

        Assert.Contains("replaced 1 occurrence(s)", result, StringComparison.Ordinal);
        Assert.Contains("The uploaded file was not changed", result, StringComparison.Ordinal);

        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("budget"));
        var page = pdf.GetPage(1);
        var words = page.GetWords().ToList();

        Assert.DoesNotContain(words, word => word.Text.Contains("2025", StringComparison.Ordinal));

        var replaced = Assert.Single(words, word => word.Text.StartsWith("2026", StringComparison.Ordinal));
        var first = replaced.Letters[0];

        Assert.Equal(700, first.StartBaseLine.Y, 1);
        Assert.Equal(14, first.PointSize, 1);
        Assert.Equal((1d, 0d, 0d), first.Color.ToRGBValues());

        // The words around the replacement stay where they were.
        Assert.Contains(words, word => word.Text == "approved.");

        using var upload = PdfDocument.Open(original);

        Assert.Contains("2025", string.Concat(upload.GetPage(1).Letters.Select(letter => letter.Value)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplaceText_OnlyTheRequestedOccurrence_AndReportsMissingText()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("notes.pdf", await PdfToolFixtures.SimplePdfAsync("Draft one and draft two and draft three."));

        var result = await host.InvokeAsync(new EditPdfContentTool(), new
        {
            operations = new object[]
            {
                new { operation = "replace_text", find = "draft", replace = "Final", occurrence = 2 },
                new { operation = "replace_text", find = "Fabrikam", replace = "Contoso" },
            },
        });

        Assert.Contains("replaced 1 occurrence(s) of \"draft\"", result, StringComparison.Ordinal);
        Assert.Contains("\"Fabrikam\" was not found", result, StringComparison.Ordinal);

        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("notes"));
        var words = pdf.GetPage(1).GetWords().Select(word => word.Text).ToList();

        Assert.Equal(2, words.Count(word => word.Equals("draft", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("Final", words);
    }

    [Fact]
    public async Task AddTextAndImage_DrawOnThePageAtTheGivenPosition()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("letter.pdf", await PdfToolFixtures.SimplePdfAsync("Dear customer,"));

        var png = "data:image/png;base64," + Convert.ToBase64String(PdfTestImages.RedSquarePng(32));
        var result = await host.InvokeAsync(new EditPdfContentTool(), new
        {
            operations = new object[]
            {
                new { operation = "add_text", page = 1, x = 300, y = 100, text = "APPROVED", font_size = 18, color = "#008000", bold = true },
                new { operation = "add_image", page = 1, x = 300, y = 140, width = 48, source = png },
            },
        });

        Assert.Contains("wrote \"APPROVED\" on page 1", result, StringComparison.Ordinal);

        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("letter"));
        var page = pdf.GetPage(1);
        var word = Assert.Single(page.GetWords(), candidate => candidate.Text == "APPROVED");

        Assert.Equal(300, word.BoundingBox.Left, 0);
        Assert.InRange(page.Height - word.BoundingBox.Top, 98, 110);

        var image = Assert.Single(page.GetImages());

        Assert.Equal(300, image.BoundingBox.Left, 0);
        Assert.Equal(48, image.BoundingBox.Width, 0);
        Assert.Equal(48, image.BoundingBox.Height, 0);
    }

    [Fact]
    public async Task RemoveArea_DeletesTheContentThere_WhileCoverOnlyHidesIt()
    {
        using var host = new PdfToolTestHost();
        var content = "BT /F1 12 Tf 72 700 Td (Remove this line) Tj 0 -40 Td (Keep this line) Tj ET";
        await host.UploadAsync("removed.pdf", PdfToolFixtures.StandardFontPdf(content));
        await host.UploadAsync("covered.pdf", PdfToolFixtures.StandardFontPdf(content));

        await host.InvokeAsync(new EditPdfContentTool(), new
        {
            pdf = "removed.pdf",
            operations = new[] { new { operation = "remove_area", page = 1, x = 60, y = 80, width = 300, height = 20 } },
        });

        var covered = await host.InvokeAsync(new EditPdfContentTool(), new
        {
            pdf = "covered.pdf",
            operations = new[] { new { operation = "cover", page = 1, x = 60, y = 80, width = 300, height = 20 } },
        });

        Assert.Contains("still in the file", covered, StringComparison.Ordinal);

        using (var removed = PdfDocument.Open(await host.ReadWorkingPdfAsync("removed")))
        {
            var text = Text(removed.GetPage(1));

            Assert.DoesNotContain("Remove", text, StringComparison.Ordinal);
            Assert.Contains("Keep", text, StringComparison.Ordinal);
        }

        using var hidden = PdfDocument.Open(await host.ReadWorkingPdfAsync("covered"));

        Assert.Contains("Remove", Text(hidden.GetPage(1)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownOperation_IsExplained()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("any.pdf", await PdfToolFixtures.SimplePdfAsync("Text"));

        var result = await host.InvokeAsync(new EditPdfContentTool(), new
        {
            operations = new[] { new { operation = "rewrite_everything" } },
        });

        Assert.Contains("is not an operation", result, StringComparison.Ordinal);
    }

    private static string Text(Page page)
    {
        return string.Join(' ', page.GetWords().Select(word => word.Text));
    }
}
