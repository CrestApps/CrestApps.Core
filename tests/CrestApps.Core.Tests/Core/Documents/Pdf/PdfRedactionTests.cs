using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Tools;
using UglyToad.PdfPig;

namespace CrestApps.Core.Tests.Core.Documents.Pdf;

public sealed class PdfRedactionTests
{
    private const string StandardFontContent =
        "BT /F1 12 Tf 72 720 Td (Customer SSN: 123-45-6789 on file.) Tj 0 -16 Td [(Email: jane) -20 (.doe@example.com here)] TJ ET";

    [Fact]
    public async Task FindThenRedactCategories_RemovesTheValuesFromTheText()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("record.pdf", PdfToolFixtures.StandardFontPdf(StandardFontContent));

        var found = await host.InvokeAsync(new FindPdfSensitiveDataTool(), new { });

        Assert.Contains("us_ssn: •••-••-6789", found, StringComparison.Ordinal);
        Assert.Contains("email: j•••@example.com", found, StringComparison.Ordinal);
        Assert.DoesNotContain("123-45-6789", found, StringComparison.Ordinal);

        var redacted = await host.InvokeAsync(new RedactPdfTool(), new { categories = new[] { "all_sensitive" }, overlay_text = "X" });

        Assert.Contains("Verified", redacted, StringComparison.Ordinal);

        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("record"));
        var text = string.Concat(pdf.GetPage(1).Letters.Select(letter => letter.Value));

        Assert.DoesNotContain("6789", text, StringComparison.Ordinal);
        Assert.DoesNotContain("example.com", text, StringComparison.Ordinal);

        // The words around the removed values are untouched.
        Assert.Contains("CustomerSSN:", text.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("onfile.", text.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("here", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RedactPhrase_InCompositeFontDocument_KeepsNeighboursInPlace()
    {
        using var host = new PdfToolTestHost();
        var original = await PdfToolFixtures.SimplePdfAsync("The merger with Northwind closes in March according to plan.");
        await host.UploadAsync("memo.pdf", original);

        double BeforeX(PdfDocument document, string word)
        {
            return document.GetPage(1).GetWords().First(candidate => candidate.Text.StartsWith(word, StringComparison.Ordinal)).BoundingBox.Left;
        }

        double originalPosition;

        using (var before = PdfDocument.Open(original))
        {
            originalPosition = BeforeX(before, "closes");
        }

        var redacted = await host.InvokeAsync(new RedactPdfTool(), new { texts = new[] { "Northwind" } });

        Assert.Contains("Verified", redacted, StringComparison.Ordinal);

        using var after = PdfDocument.Open(await host.ReadWorkingPdfAsync("memo"));
        var words = after.GetPage(1).GetWords().Select(word => word.Text).ToList();

        Assert.DoesNotContain(words, word => word.Contains("Northwind", StringComparison.Ordinal));
        Assert.Contains("closes", words);
        Assert.Equal(originalPosition, BeforeX(after, "closes"), 1);
    }

    [Fact]
    public async Task RedactAreaAndWholePage_RemovesEverythingThere()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("two.pdf", await PdfToolFixtures.SimplePdfAsync("Keep this first line", "Secret second page"));

        var result = await host.InvokeAsync(new RedactPdfTool(), new { whole_pages = "2" });

        Assert.Contains("page 2", result, StringComparison.Ordinal);

        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("two"));

        Assert.DoesNotContain(pdf.GetPage(2).Letters, letter => !string.IsNullOrWhiteSpace(letter.Value));
        Assert.Contains("Keep", string.Join(' ', pdf.GetPage(1).GetWords().Select(word => word.Text)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RedactPdf_NothingFound_ChangesNothing()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("clean.pdf", await PdfToolFixtures.SimplePdfAsync("Nothing to see"));

        var result = await host.InvokeAsync(new RedactPdfTool(), new { texts = new[] { "absent phrase" } });

        Assert.Contains("nothing was redacted", result, StringComparison.Ordinal);
        Assert.Empty((await host.LoadWorkspaceAsync()).Documents);
    }

    [Theory]
    [InlineData("4242 4242 4242 4242", true)]
    [InlineData("4242 4242 4242 4241", false)]
    public void PatternLibrary_ChecksCardNumbers(string value, bool valid)
    {
        Assert.Equal(valid, PdfPatternLibrary.IsLuhnValid(value));
    }

    [Theory]
    [InlineData("GB82 WEST 1234 5698 7654 32", true)]
    [InlineData("GB82 WEST 1234 5698 7654 33", false)]
    public void PatternLibrary_ChecksIbans(string value, bool valid)
    {
        Assert.Equal(valid, PdfPatternLibrary.IsIbanValid(value));
    }

    [Fact]
    public void PatternLibrary_FindsAndMasksValues()
    {
        var matches = PdfPatternLibrary.Find("Call 555-123-4567 or mail ops@contoso.com; SSN 000-12-3456 is not valid, 219-09-9999 is.");

        Assert.Contains(matches, match => match.Kind == PdfPatternLibrary.Phone);
        Assert.Contains(matches, match => match.Kind == PdfPatternLibrary.Email && match.Value == "ops@contoso.com");
        Assert.DoesNotContain(matches, match => match.Value == "000-12-3456" && match.Kind == PdfPatternLibrary.UsSocialSecurityNumber);
        Assert.Contains(matches, match => match.Value == "219-09-9999" && match.Kind == PdfPatternLibrary.UsSocialSecurityNumber);
        Assert.Equal("•••-••-9999", PdfPatternLibrary.Mask(PdfPatternLibrary.UsSocialSecurityNumber, "219-09-9999"));
    }
}
