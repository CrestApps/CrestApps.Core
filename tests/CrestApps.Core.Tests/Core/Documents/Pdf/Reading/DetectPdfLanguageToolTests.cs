using CrestApps.Core.AI.Documents.Pdf.Tools;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Reading;

public sealed class DetectPdfLanguageToolTests
{
    [Fact]
    public async Task DetectLanguage_EnglishDocument_MatchesItsDeclaredLanguage()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync());

        var result = await host.InvokeAsync(new DetectPdfLanguageTool());

        Assert.Contains("is written in English (en), Latin script", result, StringComparison.Ordinal);
        Assert.Contains("Declared language (catalog /Lang): en-US (English) — matches the text.", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetectLanguage_WrongDeclaredLanguage_IsFlagged()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", await PdfReadingFixtures.ReportAsync(language: "fr-FR"));

        var result = await host.InvokeAsync(new DetectPdfLanguageTool());

        Assert.Contains("fr-FR (French) — does NOT match the text, which reads as English (en)", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetectLanguage_PerPage_ReportsAMixedDocument()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync(
            "mixed.pdf",
            await PdfReadingFixtures.ParagraphsAsync(
                "Le chiffre d'affaires a augmenté de douze pour cent par rapport au plan pour le trimestre, et la croissance est répartie sur toutes les régions que nous servons.",
                "---",
                "Der Umsatz ist im Quartal um zwölf Prozent gestiegen, und das Wachstum verteilt sich auf alle Regionen, die wir mit unseren Produkten bedienen."));

        var result = await host.InvokeAsync(new DetectPdfLanguageTool(), new { per_page = true });

        Assert.Contains("The document mixes languages: page 1: French (fr); page 2: German (de).", result, StringComparison.Ordinal);
        Assert.Contains("p1: French (fr)", result, StringComparison.Ordinal);
        Assert.Contains("p2: German (de)", result, StringComparison.Ordinal);
        Assert.Contains("declares no language", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetectLanguage_PageWithoutText_CannotTell()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("scan.pdf", PdfReadingFixtures.TextAndBlankPage("Hello."));

        var result = await host.InvokeAsync(new DetectPdfLanguageTool(), new { pages = "2" });

        Assert.Contains("could not be told", result, StringComparison.Ordinal);
        Assert.Contains("ocr_pdf", result, StringComparison.Ordinal);
    }
}
