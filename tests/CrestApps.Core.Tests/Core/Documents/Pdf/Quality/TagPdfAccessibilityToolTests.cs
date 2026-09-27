using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Tools;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Quality;

public sealed class TagPdfAccessibilityToolTests
{
    [Fact]
    public async Task TagAccessibility_UntaggedForm_SetsLanguageTitleTooltipsLinksAndTabs()
    {
        var bytes = QualityTestPdfs.Helvetica(
            ["BT /F1 24 Tf 72 720 Td (Membership application) Tj ET BT /F1 12 Tf 72 650 Td (Our website) Tj ET"],
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

        var result = await host.InvokeAsync(new TagPdfAccessibilityTool(), new { pdf = "form.pdf", language = "en-US" });

        Assert.Contains("Saved as working PDF \"form\"", result, StringComparison.Ordinal);
        Assert.Contains("Set the document language to en-US.", result, StringComparison.Ordinal);
        Assert.Contains("\"Membership application\"", result, StringComparison.Ordinal);
        Assert.Contains("cannot build one", result, StringComparison.Ordinal);

        using var saved = PdfReader.Open(new MemoryStream(await host.ReadWorkingPdfAsync("form")), PdfDocumentOpenMode.Import);
        var catalog = saved.Internals.Catalog;

        Assert.Equal("en-US", PdfObjects.GetText(catalog, "/Lang"));
        Assert.Equal("Membership application", saved.Info.Title);
        Assert.True(PdfObjects.GetBoolean(PdfObjects.GetDictionary(catalog, "/ViewerPreferences"), "/DisplayDocTitle"));
        Assert.Equal("/S", PdfObjects.GetName(saved.Pages[0], "/Tabs"));

        var field = Assert.Single(PdfFormFieldList.Read(saved));

        Assert.Equal("First name", field.Tooltip);

        var link = Assert.Single(PdfAnnotationList.Read(saved, [1]), annotation => annotation.IsLink);

        Assert.Equal("Our website (link to https://example.com)", link.Contents);
        Assert.Null(PdfObjects.GetDictionary(catalog, "/MarkInfo"));

        var recheck = await host.InvokeAsync(new CheckPdfAccessibilityTool(), new { pdf = "form" });

        Assert.Contains("[PASS] Document language", recheck, StringComparison.Ordinal);
        Assert.Contains("[PASS] Form field descriptions", recheck, StringComparison.Ordinal);
        Assert.Contains("[PASS] Link descriptions", recheck, StringComparison.Ordinal);
        Assert.Contains("[PASS] Tab order", recheck, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TagAccessibility_ExplicitTooltipsAndTitle_AreApplied()
    {
        var bytes = QualityTestPdfs.Helvetica(
            ["BT /F1 12 Tf 72 700 Td (Form) Tj ET"],
            document => QualityTestPdfs.AddTextField(document, 0, "fn"));

        using var host = new PdfToolTestHost();
        await host.UploadAsync("short.pdf", bytes);

        var result = await host.InvokeAsync(new TagPdfAccessibilityTool(), new
        {
            language = "fr",
            title = "Formulaire d'adhésion",
            field_tooltips = new Dictionary<string, string> { ["fn"] = "Prénom", ["nope"] = "Unused" },
            save_as = "accessible",
        });

        Assert.Contains("Saved as working PDF \"accessible\"", result, StringComparison.Ordinal);
        Assert.Contains("No form field is named \"nope\"", result, StringComparison.Ordinal);

        using var saved = PdfReader.Open(new MemoryStream(await host.ReadWorkingPdfAsync("accessible")), PdfDocumentOpenMode.Import);

        Assert.Equal("Formulaire d'adhésion", saved.Info.Title);
        Assert.Equal("Prénom", Assert.Single(PdfFormFieldList.Read(saved)).Tooltip);
    }

    [Fact]
    public async Task TagAccessibility_NoLanguageAnywhere_AsksForOne()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("plain.pdf", QualityTestPdfs.Helvetica(["BT /F1 12 Tf 72 700 Td (Hello) Tj ET"]));

        var result = await host.InvokeAsync(new TagPdfAccessibilityTool(), new { pdf = "plain.pdf" });

        Assert.Contains("Pass 'language'", result, StringComparison.Ordinal);
        Assert.Empty((await host.LoadWorkspaceAsync()).Documents);

        var invalid = await host.InvokeAsync(new TagPdfAccessibilityTool(), new { pdf = "plain.pdf", language = "English please" });

        Assert.Contains("is not a language tag", invalid, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TagAccessibility_TaggedDocument_MarksItAndAddsFigureAltText()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("tagged.pdf", QualityTestPdfs.Tagged(marked: false));

        var result = await host.InvokeAsync(new TagPdfAccessibilityTool(), new
        {
            pdf = "tagged.pdf",
            title = "Tagged sample",
            figure_alt_texts = new[] { "A red square", "Left over" },
        });

        Assert.Contains("Marked the document as tagged", result, StringComparison.Ordinal);
        Assert.Contains("Added alternative text to 1 figure(s)", result, StringComparison.Ordinal);
        Assert.Contains("1 alternative text(s) were left over", result, StringComparison.Ordinal);

        using var saved = PdfReader.Open(new MemoryStream(await host.ReadWorkingPdfAsync("tagged")), PdfDocumentOpenMode.Import);

        Assert.True(PdfObjects.GetBoolean(PdfObjects.GetDictionary(saved.Internals.Catalog, "/MarkInfo"), "/Marked"));

        var figure = Assert.Single(PdfStructureTree.Read(saved), element => element.StandardType == "Figure");

        Assert.Equal("A red square", figure.Alt);
    }

    [Fact]
    public async Task TagAccessibility_ArchiveClaim_SurvivesSaving()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("archive.pdf", QualityTestPdfs.ArchiveCandidate());

        var result = await host.InvokeAsync(new TagPdfAccessibilityTool(), new { pdf = "archive.pdf", language = "en" });

        Assert.Contains("Kept the original XMP metadata properties", result, StringComparison.Ordinal);

        using var saved = PdfReader.Open(new MemoryStream(await host.ReadWorkingPdfAsync("archive")), PdfDocumentOpenMode.Import);
        var xmp = PdfXmpInfo.Read(saved);

        Assert.Equal(2, xmp.PdfAPart);
        Assert.Equal("B", xmp.PdfAConformance);
        Assert.Equal("Archive copy", xmp.Title);

        using var content = UglyToad.PdfPig.PdfDocument.Open(await host.ReadWorkingPdfAsync("archive"));

        Assert.Equal(1, content.NumberOfPages);
    }

    [Fact]
    public async Task TagAccessibility_ComposedDocument_SavesAFileCopy()
    {
        var definition = QualityTestPdfs.Report();

        definition.Language = null;

        using var host = new PdfToolTestHost();
        await QualityTestPdfs.AddComposedAsync(host, "report", definition);

        var result = await host.InvokeAsync(new TagPdfAccessibilityTool(), new { pdf = "report", language = "en-GB" });

        Assert.Contains("Saved as working PDF \"report-edited\"", result, StringComparison.Ordinal);
        Assert.Contains("is a composed document", result, StringComparison.Ordinal);

        var state = await host.LoadWorkspaceAsync();

        Assert.True(state.Find("report").IsComposed);
        Assert.False(state.Find("report-edited").IsComposed);
    }
}
