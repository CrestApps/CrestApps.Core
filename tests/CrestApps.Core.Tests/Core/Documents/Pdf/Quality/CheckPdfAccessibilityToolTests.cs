using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Tools;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Quality;

public sealed class CheckPdfAccessibilityToolTests
{
    [Fact]
    public async Task CheckAccessibility_ComposedDocument_HasLanguageAndTitleButNoTags()
    {
        using var host = new PdfToolTestHost();
        await QualityTestPdfs.AddComposedAsync(host, "report", QualityTestPdfs.Report());

        var result = await host.InvokeAsync(new CheckPdfAccessibilityTool(), new { pdf = "report" });

        Assert.Contains("Verdict: not accessible yet", result, StringComparison.Ordinal);
        Assert.Contains("score ", result, StringComparison.Ordinal);
        Assert.Contains("[FAIL] Tagged PDF", result, StringComparison.Ordinal);
        Assert.Contains("cannot be given a full structure tree", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] Document language: The document language is en-US.", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] Document title", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] Title in the window", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] Fonts", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] Extractable text", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAccessibility_UntaggedForm_ListsTheFixes()
    {
        var bytes = QualityTestPdfs.Helvetica(
            ["BT /F1 12 Tf 72 700 Td (Application form) Tj ET BT /F1 12 Tf 72 650 Td (Our website) Tj ET"],
            document =>
            {
                QualityTestPdfs.AddLink(document, 0, [70, 645, 170, 665], action =>
                {
                    action.Elements.SetName("/S", "/URI");
                    action.Elements.SetString("/URI", "https://example.com");
                });

                QualityTestPdfs.AddTextField(document, 0, "first_name");
            });

        using var host = new PdfToolTestHost();
        await host.UploadAsync("form.pdf", bytes);

        var result = await host.InvokeAsync(new CheckPdfAccessibilityTool(), new { pdf = "form.pdf" });

        Assert.Contains("[FAIL] Document language", result, StringComparison.Ordinal);
        Assert.Contains("[FAIL] Document title", result, StringComparison.Ordinal);
        Assert.Contains("[FAIL] Form field descriptions", result, StringComparison.Ordinal);
        Assert.Contains("\"first_name\"", result, StringComparison.Ordinal);
        Assert.Contains("[FAIL] Link descriptions", result, StringComparison.Ordinal);
        Assert.Contains("[FAIL] Tab order", result, StringComparison.Ordinal);
        Assert.Contains("[WARN] Fonts", result, StringComparison.Ordinal);
        Assert.Contains("tag_pdf_accessibility", result, StringComparison.Ordinal);
        Assert.Contains("[INFO] Bookmarks", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAccessibility_TaggedDocument_ChecksFiguresTablesAndHeadings()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("tagged.pdf", QualityTestPdfs.Tagged(marked: true));

        var result = await host.InvokeAsync(new CheckPdfAccessibilityTool(), new { pdf = "tagged.pdf" });

        Assert.Contains("[PASS] Tagged PDF", result, StringComparison.Ordinal);
        Assert.Contains("[FAIL] Alternative text: 1 of 1 figure(s) have no alternative text: figure 1 (page 1)", result, StringComparison.Ordinal);
        Assert.Contains("[FAIL] Table headers", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] Headings: The structure has 1 heading(s).", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] Document language: The document language is en-GB.", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Humanize_TurnsFieldNamesIntoWords()
    {
        Assert.Equal("First name", PdfFormFieldList.Humanize("first_name"));
        Assert.Equal("Date of birth", PdfFormFieldList.Humanize("dateOfBirth"));
        Assert.Equal("Address line 2", PdfFormFieldList.Humanize("form1[0].page1[0].address-line2[0]"));
        Assert.Null(PdfFormFieldList.Humanize(" "));
    }
}
