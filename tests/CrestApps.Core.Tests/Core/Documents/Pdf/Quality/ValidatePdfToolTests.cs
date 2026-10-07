using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Tools;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Quality;

public sealed class ValidatePdfToolTests
{
    [Fact]
    public async Task ValidatePdf_ComposedDocument_IsStructurallySound()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await QualityTestPdfs.ComposeAsync(QualityTestPdfs.Report()));

        var result = await host.InvokeAsync(new ValidatePdfTool(), new { pdf = "report.pdf" });

        Assert.Contains("Verdict: the file is structurally sound", result, StringComparison.Ordinal);
        Assert.DoesNotContain("[FAIL]", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] Content streams", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] Resources", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] Page count: 3 page(s)", result, StringComparison.Ordinal);
        Assert.Contains("[PASS] Unicode mapping", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidatePdf_DamagedContent_ReportsPagesAndResources()
    {
        var bytes = QualityTestPdfs.Raw(
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 6 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R /F9 99 0 R >> >> /Contents 4 0 R >>",
            QualityTestPdfs.Stream("BT /F2 12 Tf 72 720 Td (Hello) Tj ET"),
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 7 0 R >>",
            QualityTestPdfs.Stream("BT /F1 12 Tf 72 720 Td (Fine) Tj ET frobnicate BT /F1 12 Tf (never closed"),
        ]);

        using var host = new PdfToolTestHost();
        await host.UploadAsync("damaged.pdf", bytes);

        var result = await host.InvokeAsync(new ValidatePdfTool(), new { pdf = "damaged.pdf" });

        Assert.Contains("Verdict: the file has integrity problems", result, StringComparison.Ordinal);
        Assert.Contains("[FAIL] Resources", result, StringComparison.Ordinal);
        Assert.Contains("page 1 uses Font /F2", result, StringComparison.Ordinal);
        Assert.Contains("[FAIL] Content streams", result, StringComparison.Ordinal);
        Assert.Contains("page 2: a string that is never closed", result, StringComparison.Ordinal);
        Assert.Contains("'frobnicate'", result, StringComparison.Ordinal);
        Assert.Contains("[WARN] References", result, StringComparison.Ordinal);
        Assert.Contains("99 0 R", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidatePdf_TruncatedFile_ReportsMissingEnd()
    {
        var bytes = await QualityTestPdfs.ComposeAsync(QualityTestPdfs.Report());
        var truncated = bytes[..(bytes.Length * 2 / 3)];

        using var host = new PdfToolTestHost();
        await host.UploadAsync("truncated.pdf", truncated);

        var result = await host.InvokeAsync(new ValidatePdfTool(), new { pdf = "truncated.pdf" });

        Assert.Contains("[FAIL] End of file", result, StringComparison.Ordinal);
        Assert.Contains("truncated", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidatePdf_ScriptAndIncrementalUpdate_AreReported()
    {
        var original = QualityTestPdfs.Raw(
        [
            "<< /Type /Catalog /Pages 2 0 R /OpenAction 5 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>",
            QualityTestPdfs.Stream("0 0 1 rg 72 600 100 100 re f"),
            "<< /S /JavaScript /JS (app.alert\\('hi'\\)) >>",
        ]);

        var updated = QualityTestPdfs.AppendUpdate(original, 4, QualityTestPdfs.Stream("1 0 0 rg 72 600 100 100 re f"), 6);

        using var host = new PdfToolTestHost();
        await host.UploadAsync("scripted.pdf", updated);

        var result = await host.InvokeAsync(new ValidatePdfTool(), new { pdf = "scripted.pdf" });

        Assert.Contains("[WARN] Active content", result, StringComparison.Ordinal);
        Assert.Contains("JavaScript actions on the open action", result, StringComparison.Ordinal);
        Assert.Contains("[INFO] Incremental updates: The file was saved 1 more time(s)", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ContentParser_ReadsOperatorsAndReportsSyntax()
    {
        var clean = PdfContentParser.Parse(Encoding.ASCII.GetBytes("q BT /F1 12 Tf [(A) -20 (B)] TJ ET BI /W 1 /H 1 /BPC 8 /CS /G ID \u0001 EI Q"));

        Assert.Empty(clean.Errors);
        Assert.Empty(clean.Warnings);
        Assert.Equal(["q", "BT", "Tf", "TJ", "ET", "BI", "Q"], clean.Operations.Select(operation => operation.Operator));
        Assert.Equal(["/F1", "12"], clean.Operations[2].Operands);

        var broken = PdfContentParser.Parse(Encoding.ASCII.GetBytes("Q BT /F1 12 Tf 1 2 Td Tj ET <4G> Tj"));

        Assert.Contains(broken.Errors, error => error.Contains("hexadecimal", StringComparison.Ordinal));
        Assert.Contains(broken.Warnings, warning => warning.Contains("'Td'", StringComparison.Ordinal) || warning.Contains("'Tj'", StringComparison.Ordinal));
        Assert.Contains(broken.Warnings, warning => warning.Contains("Q without q", StringComparison.Ordinal));
        Assert.Contains(broken.Warnings, warning => warning.Contains("outside a text object", StringComparison.Ordinal));
    }
}
