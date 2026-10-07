using CrestApps.Core.AI.Documents.Pdf.Tools;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Actions;
using UglyToad.PdfPig.Annotations;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Properties;

public sealed class AddPdfLinksToolTests
{
    [Fact]
    public async Task AddLinks_OverAnAreaAndOverText_ReadBackWhereTheyWerePlaced()
    {
        using var host = new PdfToolTestHost();

        await host.UploadAsync("report.pdf", PdfPropertiesFixtures.TextPdf(pages: 3));

        var answer = await host.InvokeAsync(new AddPdfLinksTool(), new
        {
            links = new object[]
            {
                new { page = 1, x = 72, y = 50, width = 128, height = 20, url = "https://example.com/report" },
                new { page = 2, text = "help desk", url = "help@example.com" },
                new { page = 1, text = "website", target_page = 3 },
            },
        });

        Assert.Contains("Added 3 link(s)", answer, StringComparison.Ordinal);
        Assert.Contains("mailto:help@example.com", answer, StringComparison.Ordinal);

        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("report"));

        var first = pdf.GetPage(1);
        var areaLink = Assert.Single(first.GetHyperlinks(), link => link.Uri == "https://example.com/report");

        // Placed in top-left page coordinates: 50 points from the top, 20 high.
        Assert.Equal(72, areaLink.Bounds.Left, 1);
        Assert.Equal(first.Height - 50, areaLink.Bounds.Top, 1);
        Assert.Equal(first.Height - 70, areaLink.Bounds.Bottom, 1);
        Assert.Equal(128, areaLink.Bounds.Width, 1);

        var mail = Assert.Single(pdf.GetPage(2).GetHyperlinks());

        Assert.Equal("mailto:help@example.com", mail.Uri);
        Assert.Contains("help desk", mail.Text, StringComparison.Ordinal);

        var internalLink = Assert.Single(first.GetAnnotations(), annotation => annotation.Type == AnnotationType.Link && annotation.Action is not UriAction);
        var website = first.GetWords().Single(word => word.Text.StartsWith("website", StringComparison.Ordinal));

        Assert.True(internalLink.Rectangle.Left <= website.BoundingBox.Left && internalLink.Rectangle.Right >= website.BoundingBox.Right);
        Assert.True(internalLink.Rectangle.Bottom <= website.BoundingBox.Bottom && internalLink.Rectangle.Top >= website.BoundingBox.Top);
    }

    [Fact]
    public async Task AddLinks_RejectsUnsafeSchemesAndMissingText()
    {
        using var host = new PdfToolTestHost();

        await host.UploadAsync("report.pdf", PdfPropertiesFixtures.TextPdf());

        Assert.Contains("only http, https and mailto", await host.InvokeAsync(new AddPdfLinksTool(), new { links = new[] { new { page = 1, text = "website", url = "javascript:alert(1)" } } }), StringComparison.Ordinal);
        Assert.Contains("only http, https and mailto", await host.InvokeAsync(new AddPdfLinksTool(), new { links = new[] { new { page = 1, text = "website", url = "file:///etc/passwd" } } }), StringComparison.Ordinal);
        Assert.Contains("was not found", await host.InvokeAsync(new AddPdfLinksTool(), new { links = new[] { new { text = "no such words", url = "https://example.com" } } }), StringComparison.Ordinal);
        Assert.Empty((await host.LoadWorkspaceAsync()).Documents);
    }
}
