using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Tools;
using UglyToad.PdfPig;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Properties;

public sealed class ManagePdfAttachmentsToolTests
{
    [Fact]
    public async Task Attachments_AddListExtractRemove_RoundTrips()
    {
        using var host = new PdfToolTestHost();

        await host.UploadAsync("report.pdf", PdfPropertiesFixtures.TextPdf());
        await host.UploadAsync("notes.txt", Encoding.UTF8.GetBytes("Meeting notes: ship on Friday."), "text/plain");

        var added = await host.InvokeAsync(new ManagePdfAttachmentsTool(), new { pdf = "report.pdf", action = "add", source = "notes.txt", description = "Meeting notes" });

        Assert.Contains("Attached \"notes.txt\"", added, StringComparison.Ordinal);

        // A second file keeps the first: the embedded-files tree is extended, not started again.
        await host.InvokeAsync(new ManagePdfAttachmentsTool(), new { action = "add", text = "a,b\n1,2", file_name = "data.csv" });

        var listing = await host.InvokeAsync(new ManagePdfAttachmentsTool(), new { action = "list" });

        Assert.Contains("has 2 attachment(s)", listing, StringComparison.Ordinal);
        Assert.Contains("\"notes.txt\"", listing, StringComparison.Ordinal);
        Assert.Contains("\"Meeting notes\"", listing, StringComparison.Ordinal);
        Assert.Contains("text/csv", listing, StringComparison.Ordinal);

        using (var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("report")))
        {
            Assert.True(pdf.Advanced.TryGetEmbeddedFiles(out var files));
            Assert.Equal(["data.csv", "notes.txt"], files.Select(file => file.Name).Order(StringComparer.Ordinal));
            Assert.Equal("Meeting notes: ship on Friday.", Encoding.UTF8.GetString(files.Single(file => file.Name == "notes.txt").Bytes));
        }

        var extracted = await host.InvokeAsync(new ManagePdfAttachmentsTool(), new { action = "extract", name = "notes.txt" });

        Assert.Contains("[doc:1]", extracted, StringComparison.Ordinal);

        var (document, bytes) = await host.ReadMarkerAsync("[doc:1]");

        Assert.Equal("notes.txt", document.FileName);
        Assert.Equal("Meeting notes: ship on Friday.", Encoding.UTF8.GetString(bytes));

        var removed = await host.InvokeAsync(new ManagePdfAttachmentsTool(), new { action = "remove", name = "notes.txt" });

        Assert.Contains("Removed 1 attachment(s)", removed, StringComparison.Ordinal);

        using (var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("report")))
        {
            Assert.True(pdf.Advanced.TryGetEmbeddedFiles(out var files));
            Assert.Equal("data.csv", Assert.Single(files).Name);
        }

        // The removed file's bytes are gone from the file, not merely unlisted.
        Assert.DoesNotContain("ship on Friday", Encoding.Latin1.GetString(await host.ReadWorkingPdfAsync("report")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Attachments_ExtractRiskyType_IsServedAsBinary()
    {
        using var host = new PdfToolTestHost();

        await host.UploadAsync("report.pdf", PdfPropertiesFixtures.TextPdf());
        await host.InvokeAsync(new ManagePdfAttachmentsTool(), new { action = "add", text = "<script>alert(1)</script>", file_name = "page.html" });

        var extracted = await host.InvokeAsync(new ManagePdfAttachmentsTool(), new { action = "extract", name = "page.html" });

        Assert.Contains("plain binary download", extracted, StringComparison.Ordinal);

        var (document, _) = await host.ReadMarkerAsync("[doc:1]");

        Assert.Equal("application/octet-stream", document.ContentType);
    }

    [Fact]
    public async Task Attachments_ListAndExtract_SaveNothing()
    {
        using var host = new PdfToolTestHost();

        await host.UploadAsync("report.pdf", PdfPropertiesFixtures.TextPdf());

        Assert.Contains("has no attachments", await host.InvokeAsync(new ManagePdfAttachmentsTool()), StringComparison.Ordinal);
        Assert.Contains("There is no attachment named", await host.InvokeAsync(new ManagePdfAttachmentsTool(), new { action = "extract", name = "missing.txt" }), StringComparison.Ordinal);
        Assert.Contains("There is no attachment named", await host.InvokeAsync(new ManagePdfAttachmentsTool(), new { action = "remove", name = "missing.txt" }), StringComparison.Ordinal);
        Assert.Empty((await host.LoadWorkspaceAsync()).Documents);
    }
}
