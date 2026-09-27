using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Tools;
using PdfSharp.Pdf;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Quality;

public sealed class CheckPdfQualityToolTests
{
    [Fact]
    public async Task CheckQuality_ComposedWorkingDocument_MatchesItsDefinition()
    {
        using var host = new PdfToolTestHost();
        await QualityTestPdfs.AddComposedAsync(host, "report", QualityTestPdfs.Report());

        var result = await host.InvokeAsync(new CheckPdfQualityTool(), new { pdf = "report" });

        Assert.Contains("Layout compared with the document's definition", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] Page setup", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] Cover page: Page 1 is the cover, titled \"Quarterly Report\"", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] Table of contents: The table of contents is on page 2 and lists 2 of the 2 heading(s)", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] Running heads and page numbers", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] Headings: All 2 heading(s)", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] Text width", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] Font embedding", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] Blank pages", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] Content near the edge", result, StringComparison.Ordinal);
        Assert.Contains("no web address was visited", result, StringComparison.Ordinal);
        Assert.DoesNotContain("[FAIL]", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckQuality_ComposedDocumentWithMissingPicture_FailsTheComparison()
    {
        var definition = QualityTestPdfs.Report();

        definition.Sections[0].Blocks.Add(new PdfBlockDefinition
        {
            Id = "b6",
            Type = "image",
            Image = new PdfImageDefinition { Source = "asset:img404", Caption = "Regional map" },
        });

        using var host = new PdfToolTestHost();
        await QualityTestPdfs.AddComposedAsync(host, "report", definition);

        var result = await host.InvokeAsync(new CheckPdfQualityTool(), new { pdf = "report", checks = new[] { "layout" } });

        Assert.Contains("[FAIL] Pictures: The definition places 1 picture(s), but the body pages draw only 0", result, StringComparison.Ordinal);
        Assert.Contains("[WARN] Renderer warnings", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] Headings", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckQuality_Rendering_FindsBlankTinyInvisibleLightAndOverlappingText()
    {
        var bytes = QualityTestPdfs.Helvetica(
        [
            "BT /F1 12 Tf 72 700 Td (A normal line of text) Tj ET BT /F1 3 Tf 72 650 Td (tiny footnote text) Tj ET",
            string.Empty,
            "q BT 3 Tr /F1 12 Tf 72 700 Td (Hidden words) Tj ET Q BT 0.98 0.98 0.98 rg /F1 12 Tf 72 600 Td (Pale text) Tj ET",
            "BT /F1 14 Tf 72 700 Td (Overlapping first line) Tj ET BT /F1 14 Tf 74 694 Td (Overlapping second line) Tj ET",
        ]);

        using var host = new PdfToolTestHost();
        await host.UploadAsync("defects.pdf", bytes);

        var result = await host.InvokeAsync(new CheckPdfQualityTool(), new { pdf = "defects.pdf", checks = new[] { "rendering" } });

        Assert.Contains("[WARN] Blank pages: page 2 shows nothing", result, StringComparison.Ordinal);
        Assert.Contains("[WARN] Tiny text", result, StringComparison.Ordinal);
        Assert.Contains("page 1:", result, StringComparison.Ordinal);
        Assert.Contains("[WARN] Invisible text", result, StringComparison.Ordinal);
        Assert.Contains("\"Hidden\"", result, StringComparison.Ordinal);
        Assert.Contains("[WARN] Light text", result, StringComparison.Ordinal);
        Assert.Contains("\"Pale\"", result, StringComparison.Ordinal);
        Assert.Contains("[WARN] Overlapping text", result, StringComparison.Ordinal);
        Assert.Contains("page 4:", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckQuality_Overflow_FindsTextOverAndNearTheEdge()
    {
        var bytes = QualityTestPdfs.Helvetica(
        [
            "BT /F1 12 Tf 6 700 Td (Close) Tj ET",
            "BT /F1 12 Tf 580 700 Td (Runs off the page) Tj ET BT /F1 12 Tf 700 500 Td (Beyond) Tj ET",
        ]);

        using var host = new PdfToolTestHost();
        await host.UploadAsync("edges.pdf", bytes);

        var result = await host.InvokeAsync(new CheckPdfQualityTool(), new { pdf = "edges.pdf", checks = new[] { "overflow", "rendering" }, min_margin_mm = 5 });

        Assert.Contains("[WARN] Content near the edge", result, StringComparison.Ordinal);
        Assert.Contains("\"Close\"", result, StringComparison.Ordinal);
        Assert.Contains("from the left edge", result, StringComparison.Ordinal);
        Assert.Contains("[WARN] Content outside the page", result, StringComparison.Ordinal);
        Assert.Contains("lies entirely outside the visible page", result, StringComparison.Ordinal);
        Assert.Contains("\"Beyond\"", result, StringComparison.Ordinal);
        Assert.Contains("[WARN] Text cut by the page edge", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckQuality_FontsAndLinks_FlagUnembeddedFontsAndUnsafeLinks()
    {
        var bytes = QualityTestPdfs.Helvetica(
            ["BT /F1 12 Tf 72 700 Td (Click here) Tj ET BT /F1 12 Tf 72 650 Td (Our site) Tj ET BT /F1 12 Tf 72 600 Td (Section) Tj ET"],
            document =>
            {
                QualityTestPdfs.AddLink(document, 0, [70, 695, 150, 715], action =>
                {
                    action.Elements.SetName("/S", "/URI");
                    action.Elements.SetString("/URI", "javascript:alert(1)");
                });

                QualityTestPdfs.AddLink(document, 0, [70, 645, 150, 665], action =>
                {
                    action.Elements.SetName("/S", "/URI");
                    action.Elements.SetString("/URI", "https://example.com/path");
                });

                QualityTestPdfs.AddLink(document, 0, [70, 595, 70, 595], action =>
                {
                    action.Elements.SetName("/S", "/GoTo");
                    action.Elements.SetString("/D", "missing-destination");
                });
            });

        using var host = new PdfToolTestHost();
        await host.UploadAsync("links.pdf", bytes);

        var result = await host.InvokeAsync(new CheckPdfQualityTool(), new { pdf = "links.pdf", checks = new[] { "fonts", "links" } });

        Assert.Contains("[WARN] Font embedding", result, StringComparison.Ordinal);
        Assert.Contains("\"Helvetica\" (Type1, standard font, not embedded)", result, StringComparison.Ordinal);
        Assert.Contains("[FAIL] Broken or unsafe links", result, StringComparison.Ordinal);
        Assert.Contains("javascript: address", result, StringComparison.Ordinal);
        Assert.Contains("\"Click here\"", result, StringComparison.Ordinal);
        Assert.Contains("named destination \"missing-destination\" does not exist", result, StringComparison.Ordinal);
        Assert.Contains("[WARN] Questionable links", result, StringComparison.Ordinal);
        Assert.Contains("zero-size area", result, StringComparison.Ordinal);
        Assert.DoesNotContain("example.com/path", result, StringComparison.Ordinal);
        Assert.Contains("no web address was visited", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckQuality_CustomFontNotEmbedded_Fails()
    {
        var bytes = QualityTestPdfs.Helvetica(
            ["BT /F1 12 Tf 72 700 Td (Hello) Tj ET"],
            document =>
            {
                var fonts = document.Pages[0].Elements.GetDictionary("/Resources").Elements.GetDictionary("/Font");
                var font = (PdfDictionary)((PdfSharp.Pdf.Advanced.PdfReference)fonts.Elements["/F1"]).Value;

                font.Elements.SetName("/Subtype", "/TrueType");
                font.Elements.SetName("/BaseFont", "/CorporateSans-Bold");
            });

        using var host = new PdfToolTestHost();
        await host.UploadAsync("custom.pdf", bytes);

        var result = await host.InvokeAsync(new CheckPdfQualityTool(), new { checks = new[] { "fonts" } });

        Assert.Contains("[FAIL] Font embedding", result, StringComparison.Ordinal);
        Assert.Contains("\"CorporateSans-Bold\" (TrueType, not embedded)", result, StringComparison.Ordinal);
        Assert.Contains("[INFO] Font list", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckQuality_MixedPageSizes_WarnsOnLayout()
    {
        var bytes = QualityTestPdfs.Helvetica(
            ["BT /F1 12 Tf 72 700 Td (Portrait) Tj ET", "BT /F1 12 Tf 72 500 Td (Landscape) Tj ET"],
            document =>
            {
                document.Pages[1].Width = PdfSharp.Drawing.XUnit.FromPoint(792);
                document.Pages[1].Height = PdfSharp.Drawing.XUnit.FromPoint(612);
            });

        using var host = new PdfToolTestHost();
        await host.UploadAsync("mixed.pdf", bytes);

        var result = await host.InvokeAsync(new CheckPdfQualityTool(), new { checks = new[] { "layout" } });

        Assert.Contains("[WARN] Page size", result, StringComparison.Ordinal);
        Assert.Contains("Letter portrait on page 1", result, StringComparison.Ordinal);
        Assert.Contains("Letter landscape on page 2", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckQuality_UnknownCheck_IsExplained()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await QualityTestPdfs.ComposeAsync(QualityTestPdfs.Report()));

        var result = await host.InvokeAsync(new CheckPdfQualityTool(), new { checks = new[] { "spelling" } });

        Assert.Contains("\"spelling\" is not a check this tool runs", result, StringComparison.Ordinal);
    }
}
