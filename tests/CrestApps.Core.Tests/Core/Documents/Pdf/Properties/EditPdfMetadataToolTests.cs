using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Rendering;
using CrestApps.Core.AI.Documents.Pdf.Tools;
using UglyToad.PdfPig;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Properties;

public sealed class EditPdfMetadataToolTests
{
    [Fact]
    public async Task EditMetadata_SetsInformationAndXmpThatViewersRead()
    {
        using var host = new PdfToolTestHost();

        await host.UploadAsync("report.pdf", PdfPropertiesFixtures.TextPdf());

        var answer = await host.InvokeAsync(new EditPdfMetadataTool(), new
        {
            title = "Q3 Results & <Outlook>",
            author = "Jane Doe",
            subject = "Quarterly review",
            keywords = "finance, q3",
            language = "en-US",
            custom = new Dictionary<string, string> { ["Department"] = "Finance" },
        });

        Assert.Contains("Saved as working PDF \"report\"", answer, StringComparison.Ordinal);
        Assert.Contains("Title: \"Old Title\" → \"Q3 Results & <Outlook>\"", answer, StringComparison.Ordinal);
        Assert.Contains("Author: \"Old Author\" → \"Jane Doe\"", answer, StringComparison.Ordinal);
        Assert.Contains("Department", answer, StringComparison.Ordinal);

        var bytes = await host.ReadWorkingPdfAsync("report");

        using var pdf = PdfDocument.Open(bytes);

        Assert.Equal("Q3 Results & <Outlook>", pdf.Information.Title);
        Assert.Equal("Jane Doe", pdf.Information.Author);
        Assert.Equal("Quarterly review", pdf.Information.Subject);
        Assert.Equal("finance, q3", pdf.Information.Keywords);
        Assert.Equal("Finance", PdfPigTokens.GetText(pdf, pdf.Information.DocumentInformationDictionary, "Department"));
        Assert.True(pdf.TryGetXmpMetadata(out var xmp));

        var rdf = PdfXmpPacket.Parse(xmp.GetXmlBytes().ToArray());

        Assert.NotNull(rdf);
        Assert.Equal("Q3 Results & <Outlook>", PdfXmpPacket.ReadProperty(rdf, PdfXmpPacket.DublinCore, "title"));
        Assert.Equal("Jane Doe", PdfXmpPacket.ReadProperty(rdf, PdfXmpPacket.DublinCore, "creator"));
        Assert.Equal("en-US", PdfXmpPacket.ReadProperty(rdf, PdfXmpPacket.DublinCore, "language"));

        // The stale packet that named the old title is not left in the file.
        var text = Encoding.Latin1.GetString(bytes);

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "xpacket begin"));
        Assert.DoesNotContain("Old Title", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditMetadata_KeepsPdfAIdentification()
    {
        using var host = new PdfToolTestHost();

        await host.UploadAsync("archive.pdf", PdfPropertiesFixtures.TextPdf(pdfA: true));

        var answer = await host.InvokeAsync(new EditPdfMetadataTool(), new { title = "Archived" });

        Assert.Contains("PDF/A-1A identification was kept", answer, StringComparison.Ordinal);

        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("archive"));

        Assert.True(pdf.TryGetXmpMetadata(out var xmp));

        var rdf = PdfXmpPacket.Parse(xmp.GetXmlBytes().ToArray());

        Assert.Equal(["PDF/A-1A"], PdfXmpPacket.DescribeConformance(rdf));
        Assert.Equal("Archived", PdfXmpPacket.ReadProperty(rdf, PdfXmpPacket.DublinCore, "title"));
    }

    [Fact]
    public async Task EditMetadata_WithoutChanges_ReportsWithoutSaving()
    {
        using var host = new PdfToolTestHost();

        await host.UploadAsync("report.pdf", PdfPropertiesFixtures.TextPdf());

        var answer = await host.InvokeAsync(new EditPdfMetadataTool());

        Assert.Contains("Title: \"Old Title\"", answer, StringComparison.Ordinal);
        Assert.Contains("Author: \"Old Author\"", answer, StringComparison.Ordinal);
        Assert.Contains("XMP metadata: present", answer, StringComparison.Ordinal);
        Assert.Empty((await host.LoadWorkspaceAsync()).Documents);
    }

    [Fact]
    public async Task EditMetadata_ClearsFieldsAndCustomProperties()
    {
        using var host = new PdfToolTestHost();

        await host.UploadAsync("report.pdf", PdfPropertiesFixtures.TextPdf(customize: document =>
        {
            document.Info.Elements.SetString("/Department", "Sales");
            document.Language = "de";
        }));

        var answer = await host.InvokeAsync(new EditPdfMetadataTool(), new { clear = new[] { "author", "language", "Department" } });

        Assert.Contains("Author: \"Old Author\" → (none)", answer, StringComparison.Ordinal);
        Assert.Contains("Language: \"de\" → (none)", answer, StringComparison.Ordinal);

        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("report"));

        Assert.True(string.IsNullOrEmpty(pdf.Information.Author));
        Assert.False(pdf.Information.DocumentInformationDictionary.Data.ContainsKey("Department"));
        Assert.False(pdf.Structure.Catalog.CatalogDictionary.Data.ContainsKey("Lang"));
    }

    [Fact]
    public async Task EditMetadata_RejectsInvalidLanguageAndReservedNames()
    {
        using var host = new PdfToolTestHost();

        await host.UploadAsync("report.pdf", PdfPropertiesFixtures.TextPdf());

        Assert.Contains("not a language tag", await host.InvokeAsync(new EditPdfMetadataTool(), new { language = "English please" }), StringComparison.Ordinal);
        Assert.Contains("standard property", await host.InvokeAsync(new EditPdfMetadataTool(), new { custom = new Dictionary<string, string> { ["Producer"] = "Me" } }), StringComparison.Ordinal);
        Assert.Empty((await host.LoadWorkspaceAsync()).Documents);
    }
}
