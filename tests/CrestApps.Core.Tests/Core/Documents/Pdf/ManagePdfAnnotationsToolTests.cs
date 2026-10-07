using CrestApps.Core.AI.Documents.Pdf.Tools;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Tokens;

namespace CrestApps.Core.Tests.Core.Documents.Pdf;

public sealed class ManagePdfAnnotationsToolTests
{
    private const string Content =
        "BT /F1 12 Tf 72 700 Td (Net revenue grew to 4.2 million this year.) Tj 0 -20 Td (The forecast assumes stable prices.) Tj ET";

    [Fact]
    public async Task Add_MarksTextAndPlacesShapes_EachWithItsOwnAppearance()
    {
        using var host = new PdfToolTestHost();
        var original = PdfToolFixtures.StandardFontPdf(Content);
        await host.UploadAsync("report.pdf", original);

        var result = await host.InvokeAsync(new ManagePdfAnnotationsTool(), new
        {
            action = "add",
            annotations = new object[]
            {
                new { type = "highlight", text = "4.2 million", contents = "Check against the ledger", author = "Reviewer" },
                new { type = "strikeout", text = "stable" },
                new { type = "note", text = "forecast", contents = "Which scenario?" },
                new { type = "stamp", page = 1, x = 400, y = 40, contents = "Draft" },
                new { type = "rectangle", page = 1, x = 60, y = 200, width = 200, height = 50, fill_color = "#FFF3E0" },
                new { type = "free_text", page = 1, x = 72, y = 300, contents = "Figures are unaudited.", font_size = 10 },
                new { type = "arrow", page = 1, x = 300, y = 320, x2 = 200, y2 = 305 },
            },
        });

        Assert.Contains("added highlight", result, StringComparison.Ordinal);
        Assert.Contains("over \"4.2 million\"", result, StringComparison.Ordinal);
        Assert.Contains("The uploaded file was not changed", result, StringComparison.Ordinal);

        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("report"));
        var annotations = pdf.GetPage(1).GetAnnotations().ToList();

        Assert.Equal(7, annotations.Count);
        Assert.All(annotations, annotation => Assert.True(annotation.AnnotationDictionary.ContainsKey(NameToken.Ap), $"{annotation.Type} has no appearance"));

        var highlight = Assert.Single(annotations, annotation => annotation.Type == AnnotationType.Highlight);

        Assert.Equal("Check against the ledger", highlight.Content);
        Assert.NotEmpty(highlight.QuadPoints);

        var stamp = Assert.Single(annotations, annotation => annotation.Type == AnnotationType.Stamp);

        Assert.Equal("Draft", stamp.Content);
        Assert.Contains(annotations, annotation => annotation.Type == AnnotationType.Line);
        Assert.Contains(annotations, annotation => annotation.Type == AnnotationType.FreeText);

        using var upload = PdfDocument.Open(original);

        Assert.Empty(upload.GetPage(1).GetAnnotations());
    }

    [Fact]
    public async Task ListUpdateRemove_WorkByTheIdsTheListReturns()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", PdfToolFixtures.StandardFontPdf(Content));

        await host.InvokeAsync(new ManagePdfAnnotationsTool(), new
        {
            action = "add",
            annotations = new object[]
            {
                new { type = "highlight", text = "Net revenue", author = "Reviewer" },
                new { type = "note", page = 1, x = 500, y = 90, contents = "First pass" },
            },
        });

        var listed = await host.InvokeAsync(new ManagePdfAnnotationsTool(), new { pdf = "report" });

        Assert.Contains("2 annotation(s)", listed, StringComparison.Ordinal);
        Assert.Contains("highlight", listed, StringComparison.Ordinal);
        Assert.Contains("over \"Net revenue\"", listed, StringComparison.Ordinal);
        Assert.Contains("by Reviewer", listed, StringComparison.Ordinal);
        Assert.Contains("\"First pass\"", listed, StringComparison.Ordinal);

        var noteId = IdOf(listed, "note");

        await host.InvokeAsync(new ManagePdfAnnotationsTool(), new
        {
            action = "update",
            pdf = "report",
            annotations = new[] { new { id = noteId, contents = "Second pass", color = "#90CAF9" } },
        });

        var updated = await host.InvokeAsync(new ManagePdfAnnotationsTool(), new { pdf = "report", types = new[] { "note" } });

        Assert.Contains("\"Second pass\"", updated, StringComparison.Ordinal);
        Assert.Contains("#90CAF9", updated, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("highlight", updated, StringComparison.Ordinal);

        await host.InvokeAsync(new ManagePdfAnnotationsTool(), new { action = "remove", pdf = "report", types = new[] { "highlight" } });

        var afterType = await host.InvokeAsync(new ManagePdfAnnotationsTool(), new { pdf = "report" });

        Assert.Contains("1 annotation(s)", afterType, StringComparison.Ordinal);
        Assert.DoesNotContain("highlight", afterType, StringComparison.Ordinal);

        await host.InvokeAsync(new ManagePdfAnnotationsTool(), new { action = "remove", pdf = "report", ids = new[] { noteId } });

        var empty = await host.InvokeAsync(new ManagePdfAnnotationsTool(), new { pdf = "report" });

        Assert.Contains("There are no comments or marks", empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Add_TextNotFound_IsReportedAndNothingChanges()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("report.pdf", PdfToolFixtures.StandardFontPdf(Content));

        var result = await host.InvokeAsync(new ManagePdfAnnotationsTool(), new
        {
            action = "add",
            annotations = new[] { new { type = "highlight", text = "Fabrikam" } },
        });

        Assert.Contains("Nothing was changed", result, StringComparison.Ordinal);
        Assert.Contains("\"Fabrikam\" was not found", result, StringComparison.Ordinal);
        Assert.Empty((await host.LoadWorkspaceAsync()).Documents);
    }

    private static string IdOf(string listing, string type)
    {
        var line = listing.Split('\n').First(candidate => candidate.Contains("] p. 1 " + type, StringComparison.Ordinal));
        var start = line.IndexOf('[', StringComparison.Ordinal) + 1;

        return line[start..line.IndexOf(']', StringComparison.Ordinal)];
    }
}
