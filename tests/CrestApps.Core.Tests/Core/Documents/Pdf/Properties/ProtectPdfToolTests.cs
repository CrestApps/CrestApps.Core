using CrestApps.Core.AI.Documents.Pdf.Tools;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Properties;

public sealed class ProtectPdfToolTests
{
    [Fact]
    public async Task ProtectThenRemoveSecurity_RoundTrips()
    {
        using var host = new PdfToolTestHost();

        await host.UploadAsync("report.pdf", PdfPropertiesFixtures.TextPdf());

        var protectedAnswer = await host.InvokeAsync(new ProtectPdfTool(), new
        {
            user_password = "open-sesame",
            owner_password = "owner-secret",
            permissions = new { print = false, copy = false },
        });

        Assert.Contains("encrypted with AES-256", protectedAnswer, StringComparison.Ordinal);
        Assert.Contains("printing", protectedAnswer, StringComparison.Ordinal);
        Assert.DoesNotContain("open-sesame", protectedAnswer, StringComparison.Ordinal);
        Assert.DoesNotContain("owner-secret", protectedAnswer, StringComparison.Ordinal);

        var locked = await host.ReadWorkingPdfAsync("report");

        Assert.ThrowsAny<PdfDocumentEncryptedException>(() => PdfDocument.Open(locked).Dispose());

        using (var opened = PdfDocument.Open(locked, new ParsingOptions { Password = "open-sesame" }))
        {
            Assert.Equal(2, opened.NumberOfPages);
        }

        using (var owner = PdfReader.Open(new MemoryStream(locked), "owner-secret", PdfDocumentOpenMode.Modify))
        {
            Assert.False(owner.SecuritySettings.PermitPrint);
            Assert.False(owner.SecuritySettings.PermitExtractContent);
            Assert.True(owner.SecuritySettings.PermitAnnotations);
        }

        // The open password does not grant the owner's rights, so it cannot remove the protection.
        Assert.Contains("needs the owner password", await host.InvokeAsync(new RemovePdfSecurityTool(), new { password = "open-sesame" }), StringComparison.Ordinal);

        var removed = await host.InvokeAsync(new RemovePdfSecurityTool(), new { password = "owner-secret" });

        Assert.Contains("no password and no permission restrictions", removed, StringComparison.Ordinal);
        Assert.DoesNotContain("owner-secret", removed, StringComparison.Ordinal);

        using var unlocked = PdfDocument.Open(await host.ReadWorkingPdfAsync("report"));

        Assert.False(unlocked.IsEncrypted);
        Assert.Equal(2, unlocked.NumberOfPages);
        Assert.Contains("help desk", unlocked.GetPage(1).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Protect_PermissionsOnly_GeneratesAnOwnerPasswordWithoutRevealingIt()
    {
        using var host = new PdfToolTestHost();

        await host.UploadAsync("report.pdf", PdfPropertiesFixtures.TextPdf());

        var answer = await host.InvokeAsync(new ProtectPdfTool(), new { permissions = new { modify = false }, encryption = "aes128" });

        Assert.Contains("encrypted with AES-128", answer, StringComparison.Ordinal);
        Assert.Contains("random one was generated and not kept", answer, StringComparison.Ordinal);
        Assert.Contains("Opening: no password is needed", answer, StringComparison.Ordinal);

        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("report"));

        Assert.True(pdf.IsEncrypted);
        Assert.Equal(2, pdf.NumberOfPages);
    }

    [Fact]
    public async Task EditingAProtectedPdf_SaysTheCopyIsNoLongerProtected()
    {
        using var host = new PdfToolTestHost();

        await host.UploadAsync("report.pdf", PdfPropertiesFixtures.TextPdf());
        await host.InvokeAsync(new ProtectPdfTool(), new { user_password = "open-sesame", owner_password = "owner-secret" });

        var answer = await host.InvokeAsync(new EditPdfMetadataTool(), new { title = "Renamed", password = "owner-secret" });

        Assert.Contains("saved without that protection", answer, StringComparison.Ordinal);

        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("report"));

        Assert.False(pdf.IsEncrypted);
        Assert.Equal("Renamed", pdf.Information.Title);
    }

    [Fact]
    public async Task RemoveSecurity_OnAnUnprotectedPdf_SavesNothing()
    {
        using var host = new PdfToolTestHost();

        await host.UploadAsync("report.pdf", PdfPropertiesFixtures.TextPdf());

        Assert.Contains("is not password protected", await host.InvokeAsync(new RemovePdfSecurityTool(), new { password = "anything" }), StringComparison.Ordinal);
        Assert.Contains("Say how to protect", await host.InvokeAsync(new ProtectPdfTool()), StringComparison.Ordinal);
        Assert.Empty((await host.LoadWorkspaceAsync()).Documents);
    }
}
