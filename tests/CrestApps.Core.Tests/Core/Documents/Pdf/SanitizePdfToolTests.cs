using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Tools;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace CrestApps.Core.Tests.Core.Documents.Pdf;

public sealed class SanitizePdfToolTests
{
    [Fact]
    public async Task Sanitize_RemovesHiddenInformation_AndKeepsWhatThePagesShow()
    {
        using var host = new PdfToolTestHost();
        var original = LoadedPdf();
        await host.UploadAsync("contract.pdf", original);

        var result = await host.InvokeAsync(new SanitizePdfTool(), new { });

        Assert.Contains("Saved working PDF \"contract\"", result, StringComparison.Ordinal);
        Assert.Contains("1 attached file(s)", result, StringComparison.Ordinal);
        Assert.Contains("1 hidden layer(s)", result, StringComparison.Ordinal);
        Assert.Contains("Still in the file", result, StringComparison.Ordinal);
        Assert.Contains("1 comment(s)", result, StringComparison.Ordinal);
        Assert.Contains("1 link(s)", result, StringComparison.Ordinal);

        var bytes = await host.ReadWorkingPdfAsync("contract");

        using (var pig = PigDocument.Open(bytes))
        {
            var text = string.Join(' ', pig.GetPage(1).GetWords().Select(word => word.Text));

            Assert.Contains("Visible", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Invisible", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Draft", text, StringComparison.Ordinal);
            Assert.Null(pig.Information.Author);
            Assert.Empty(pig.Advanced.TryGetEmbeddedFiles(out var files) ? files : []);
        }

        using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        var catalog = document.Internals.Catalog;

        Assert.False(catalog.Elements.ContainsKey("/OpenAction"));
        Assert.False(catalog.Elements.ContainsKey("/OCProperties"));
        Assert.False(document.Pages[0].Elements.ContainsKey("/Thumb"));
        Assert.DoesNotContain("Jane Roe", Encoding.Latin1.GetString(bytes), StringComparison.Ordinal);

        // The comment and the link are visible, so they stay unless asked for.
        Assert.Equal(2, ((PdfArray)document.Pages[0].Elements.GetObject("/Annots")).Elements.Count);

        using var upload = PigDocument.Open(original);

        Assert.Equal("Jane Roe", upload.Information.Author);
    }

    [Fact]
    public async Task Sanitize_ReportOnly_SavesNothing_AndNamedVisibleKindsAreRemoved()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("contract.pdf", LoadedPdf());

        var report = await host.InvokeAsync(new SanitizePdfTool(), new { report_only = true });

        Assert.Contains("Nothing was saved", report, StringComparison.Ordinal);
        Assert.Contains("1 comment(s) and mark(s) (not selected)", report, StringComparison.Ordinal);
        Assert.Empty((await host.LoadWorkspaceAsync()).Documents);

        var result = await host.InvokeAsync(new SanitizePdfTool(), new { remove = new[] { "comments", "external_links" } });

        Assert.Contains("1 comment(s) and mark(s)", result, StringComparison.Ordinal);

        using var document = PdfReader.Open(new MemoryStream(await host.ReadWorkingPdfAsync("contract")), PdfDocumentOpenMode.Import);
        var annotations = document.Pages[0].Elements.GetObject("/Annots") as PdfArray;

        Assert.True(annotations is null || annotations.Elements.Count == 0);

        // Only what was named is removed; the metadata stays.
        Assert.Equal("Jane Roe", document.Info.Author);
    }

    [Fact]
    public async Task Sanitize_UnknownKind_IsExplained()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("contract.pdf", LoadedPdf());

        var result = await host.InvokeAsync(new SanitizePdfTool(), new { remove = new[] { "everything_secret" } });

        Assert.Contains("'everything_secret' cannot be removed", result, StringComparison.Ordinal);
    }

    private static byte[] LoadedPdf()
    {
        const string Content =
            "BT /F1 12 Tf 72 700 Td (Visible terms) Tj ET\n" +
            "BT /F1 12 Tf 3 Tr 72 680 Td (Invisible note) Tj ET\n" +
            "/OC /L1 BDC BT /F1 12 Tf 0 Tr 72 660 Td (Draft wording) Tj ET EMC";

        using var document = PdfReader.Open(new MemoryStream(PdfToolFixtures.StandardFontPdf(Content)), PdfDocumentOpenMode.Modify);
        var catalog = document.Internals.Catalog;
        var page = document.Pages[0];

        document.Info.Author = "Jane Roe";
        document.Info.Title = "Contoso contract";

        var script = new PdfDictionary(document);
        script.Elements.SetName("/S", "/JavaScript");
        script.Elements["/JS"] = new PdfString("app.alert('opened');");
        catalog.Elements["/OpenAction"] = script;

        var draft = new PdfDictionary(document);
        draft.Elements.SetName("/Type", "/OCG");
        draft.Elements["/Name"] = new PdfString("Draft");
        document.Internals.AddObject(draft);

        var groups = new PdfArray(document);
        groups.Elements.Add(draft.Reference);

        var off = new PdfArray(document);
        off.Elements.Add(draft.Reference);

        var configuration = new PdfDictionary(document);
        configuration.Elements["/OFF"] = off;

        var properties = new PdfDictionary(document);
        properties.Elements["/OCGs"] = groups;
        properties.Elements["/D"] = configuration;
        catalog.Elements["/OCProperties"] = properties;

        var used = new PdfDictionary(document);
        used.Elements["/L1"] = draft.Reference;
        page.Resources.Elements["/Properties"] = used;

        var thumbnail = new PdfDictionary(document);
        thumbnail.CreateStream([0, 0, 0]);
        document.Internals.AddObject(thumbnail);
        page.Elements["/Thumb"] = thumbnail.Reference;

        PdfAttachments.Add(document, "pricing.csv", Encoding.UTF8.GetBytes("item,price\nwidget,10\n"), "Pricing", "text/csv", DateTimeOffset.UnixEpoch, associate: false);

        var annotations = new PdfArray(document);
        page.Elements["/Annots"] = annotations;

        var comment = new PdfDictionary(document);
        comment.Elements.SetName("/Type", "/Annot");
        comment.Elements.SetName("/Subtype", "/Text");
        comment.Elements["/Rect"] = new PdfArray(document, new PdfReal(300), new PdfReal(700), new PdfReal(318), new PdfReal(718));
        comment.Elements["/Contents"] = new PdfString("Internal: do not share");
        document.Internals.AddObject(comment);
        annotations.Elements.Add(comment.Reference);

        var uri = new PdfDictionary(document);
        uri.Elements.SetName("/S", "/URI");
        uri.Elements["/URI"] = new PdfString("https://example.com/terms");

        var link = new PdfDictionary(document);
        link.Elements.SetName("/Type", "/Annot");
        link.Elements.SetName("/Subtype", "/Link");
        link.Elements["/Rect"] = new PdfArray(document, new PdfReal(72), new PdfReal(695), new PdfReal(160), new PdfReal(712));
        link.Elements["/A"] = uri;
        document.Internals.AddObject(link);
        annotations.Elements.Add(link.Reference);

        using var buffer = new MemoryStream();
        document.Save(buffer, closeStream: false);

        return buffer.ToArray();
    }
}
