using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Tools;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Reading;

public sealed class ExtractPdfLinksToolTests
{
    [Fact]
    public async Task ExtractLinks_ListsWebEmailScriptAndInternalLinks()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("links.pdf", PdfReadingFixtures.LinksAndBookmarks());

        var result = await host.InvokeAsync(new ExtractPdfLinksTool());

        Assert.Contains("4 link(s) in \"links.pdf\"", result, StringComparison.Ordinal);
        Assert.Contains("p1 web: \"Visit our website for details.\" → https://example.com/details", result, StringComparison.Ordinal);
        Assert.Contains("p1 email: \"Write to the team.\" → mailto:team@example.com", result, StringComparison.Ordinal);
        Assert.Contains("p1 javascript: \"Run the script.\" → javascript:alert(1)", result, StringComparison.Ordinal);
        Assert.Contains("p1 internal: \"Go to the appendix.\" → page 2", result, StringComparison.Ordinal);
        Assert.DoesNotContain(" — ok", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractLinks_Validate_FlagsRiskyLinksWithoutOpeningAny()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("links.pdf", PdfReadingFixtures.LinksAndBookmarks());

        var result = await host.InvokeAsync(new ExtractPdfLinksTool(), new { validate = true });

        Assert.Contains("1 link(s) need attention", result, StringComparison.Ordinal);
        Assert.Contains("javascript:alert(1) [", result, StringComparison.Ordinal);
        Assert.Contains("— risky: runs a script when clicked", result, StringComparison.Ordinal);
        Assert.Contains("→ page 2 [", result, StringComparison.Ordinal);
        Assert.Contains("No link was opened", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractLinks_PageWithoutLinks_SaysSo()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("links.pdf", PdfReadingFixtures.LinksAndBookmarks());

        var result = await host.InvokeAsync(new ExtractPdfLinksTool(), new { pages = "2" });

        Assert.Contains("No links found", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://example.com/a", null, "ok")]
    [InlineData("http://example.com", null, "ok")]
    [InlineData("mailto:someone@example.com", null, "ok")]
    [InlineData("www.example.com", null, "invalid")]
    [InlineData("file:///C:/secrets.txt", null, "risky")]
    [InlineData("javascript:void(0)", null, "risky")]
    [InlineData("https://evil.example.net/login", "https://bank.example.com", "risky")]
    [InlineData("https://example.com/page", "www.example.com/page", "ok")]
    [InlineData("gopher://example.com", null, "unusual")]
    public void ValidateUri_ChecksFormAndSafety(string target, string text, string expected)
    {
        var (status, _) = PdfLinkReader.ValidateUri(target, text);

        Assert.Equal(expected, status);
    }
}
