using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Tools;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Quality;

public sealed class ValidatePdfComplianceToolTests
{
    [Fact]
    public async Task ValidateCompliance_NoClaims_ChecksPdfA2bAndPdfUA1()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await QualityTestPdfs.ComposeAsync(QualityTestPdfs.Report()));

        var result = await host.InvokeAsync(new ValidatePdfComplianceTool(), new { pdf = "report.pdf" });

        Assert.Contains("checked: PDF/A-2b, PDF/UA-1", result, StringComparison.Ordinal);
        Assert.Contains("Verdict: does not satisfy PDF/A-2b; does not satisfy PDF/UA-1", result, StringComparison.Ordinal);
        Assert.Contains("[FAIL] PDF/A identification", result, StringComparison.Ordinal);
        Assert.Contains("[FAIL] Output intent", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] Fonts embedded", result, StringComparison.Ordinal);
        Assert.Contains("[FAIL] Tagged PDF", result, StringComparison.Ordinal);
        Assert.Contains("claims neither PDF/A nor PDF/UA", result, StringComparison.Ordinal);
        Assert.Contains("not a replacement for a conformance validator such as veraPDF", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateCompliance_ArchiveCandidate_AppearsToSatisfyItsClaim()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("archive.pdf", QualityTestPdfs.ArchiveCandidate());

        var result = await host.InvokeAsync(new ValidatePdfComplianceTool(), new { pdf = "archive.pdf", standard = "auto" });

        Assert.Contains("checked: PDF/A-2b.", result, StringComparison.Ordinal);
        Assert.Contains("Verdict: appears to satisfy PDF/A-2b", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] PDF/A identification", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] Output intent", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] Document ID", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] Metadata consistency", result, StringComparison.Ordinal);
        Assert.DoesNotContain("[FAIL]", result, StringComparison.Ordinal);
        Assert.Contains("veraPDF", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateCompliance_ExplicitStandard_ChecksThatStandard()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("archive.pdf", QualityTestPdfs.ArchiveCandidate());

        var result = await host.InvokeAsync(new ValidatePdfComplianceTool(), new { standard = "pdfa-1b" });

        Assert.Contains("Verdict: does not satisfy PDF/A-1b", result, StringComparison.Ordinal);
        Assert.Contains("claims PDF/A-2b, not PDF/A-1b", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateCompliance_ScriptsAndStandardFonts_FailPdfA()
    {
        var bytes = QualityTestPdfs.Helvetica(
            ["BT /F1 12 Tf 72 700 Td (Hello) Tj ET"],
            document => QualityTestPdfs.AddLink(document, 0, [70, 695, 150, 715], action =>
            {
                action.Elements.SetName("/S", "/JavaScript");
                action.Elements.SetString("/JS", "app.alert('hi')");
            }));

        using var host = new PdfToolTestHost();
        await host.UploadAsync("scripted.pdf", bytes);

        var result = await host.InvokeAsync(new ValidatePdfComplianceTool(), new { standard = "pdfa-2b" });

        Assert.Contains("Verdict: does not satisfy PDF/A-2b", result, StringComparison.Ordinal);
        Assert.Contains("[FAIL] Fonts embedded", result, StringComparison.Ordinal);
        Assert.Contains("\"Helvetica\"", result, StringComparison.Ordinal);
        Assert.Contains("[FAIL] Scripts and actions", result, StringComparison.Ordinal);
        Assert.Contains("a link on page 1", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateCompliance_PdfUA_ChecksAccessibilityRequirements()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("tagged.pdf", QualityTestPdfs.Tagged(marked: true));

        var result = await host.InvokeAsync(new ValidatePdfComplianceTool(), new { standard = "pdfua-1" });

        Assert.Contains("Verdict: does not satisfy PDF/UA-1", result, StringComparison.Ordinal);
        Assert.Contains("[FAIL] PDF/UA identification", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] Tagged PDF", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] Document language", result, StringComparison.Ordinal);
        Assert.Contains("[FAIL] Figures", result, StringComparison.Ordinal);
        Assert.Contains("[WARN] Table headers", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateCompliance_UnknownStandard_IsExplained()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("archive.pdf", QualityTestPdfs.ArchiveCandidate());

        var result = await host.InvokeAsync(new ValidatePdfComplianceTool(), new { standard = "iso-9001" });

        Assert.Contains("\"iso-9001\" is not a standard this tool checks", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pdfa-2b", "PDF/A-2b")]
    [InlineData("PDF/A-1b", "PDF/A-1b")]
    [InlineData("pdfa-3u", "PDF/A-3u")]
    [InlineData("pdfua-1", "PDF/UA-1")]
    [InlineData("PDF/UA", "PDF/UA-1")]
    public void Standard_ParsesCommonSpellings(string text, string name)
    {
        Assert.True(PdfStandard.TryParse(text, out var standard));
        Assert.Equal(name, standard.Name);
    }

    [Fact]
    public void Standard_RejectsUnknownOnes()
    {
        Assert.False(PdfStandard.TryParse("pdfa-1u", out _));
        Assert.False(PdfStandard.TryParse("pdfx-4", out _));
    }
}
