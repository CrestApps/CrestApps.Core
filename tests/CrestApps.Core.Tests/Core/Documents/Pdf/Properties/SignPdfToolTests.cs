using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf;
using CrestApps.Core.AI.Documents.Pdf.Tools;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Properties;

public sealed class SignPdfToolTests
{
    [Fact]
    public async Task SignThenVerify_ReportsAValidSignatureFromTheConfiguredSigner()
    {
        using var certificate = CreateCertificate();
        using var host = CreateHost(certificate);

        await host.UploadAsync("contract.pdf", PdfPropertiesFixtures.TextPdf());

        var signed = await host.InvokeAsync(new SignPdfTool(), new { reason = "Approved", location = "Toronto" });

        Assert.Contains("Signed. Saved as working PDF \"contract\"", signed, StringComparison.Ordinal);
        Assert.Contains("CN=Test Signer", signed, StringComparison.Ordinal);
        Assert.Contains("invisible", signed, StringComparison.Ordinal);
        Assert.Contains("not trusted on this server", signed, StringComparison.Ordinal);

        var verified = await host.InvokeAsync(new VerifyPdfSignatureTool(), new { pdf = "contract" });

        Assert.Contains("1 signature(s): 1 valid, 0 invalid, 0 modified after signing", verified, StringComparison.Ordinal);
        Assert.Contains("— VALID", verified, StringComparison.Ordinal);
        Assert.Contains("CN=Test Signer", verified, StringComparison.Ordinal);
        Assert.Contains("reason \"Approved\"", verified, StringComparison.Ordinal);
        Assert.Contains("covers the whole file", verified, StringComparison.Ordinal);
        Assert.Contains("UTC (signed by the signer)", verified, StringComparison.Ordinal);
        Assert.Contains("not trusted on this server", verified, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sign_VisibleBox_IsPlacedOnThePage()
    {
        using var certificate = CreateCertificate();
        using var host = CreateHost(certificate);

        await host.UploadAsync("contract.pdf", PdfPropertiesFixtures.TextPdf());

        var signed = await host.InvokeAsync(new SignPdfTool(), new { page = 2, x = 72, y = 600, width = 180, height = 60 });

        Assert.Contains("visible on page 2 at x=72, y=600, w=180, h=60", signed, StringComparison.Ordinal);

        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("contract"));

        Assert.True(pdf.TryGetForm(out var form));

        var field = Assert.Single(form.Fields);

        Assert.Equal(2, field.PageNumber);
        Assert.Equal(72, field.Bounds!.Value.Left, 1);
        Assert.Equal(pdf.GetPage(2).Height - 600, field.Bounds.Value.Top, 1);
        Assert.Contains("visible on page 2", await host.InvokeAsync(new VerifyPdfSignatureTool()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verify_DetectsChangesInsideAndAfterTheSignedBytes()
    {
        using var certificate = CreateCertificate();
        using var host = CreateHost(certificate);

        await host.UploadAsync("contract.pdf", PdfPropertiesFixtures.TextPdf());
        await host.InvokeAsync(new SignPdfTool());

        var signed = await host.ReadWorkingPdfAsync("contract");

        // Bytes appended after signing: the signed revision is intact but no longer the whole file.
        await host.UploadAsync("appended.pdf", [.. signed, .. Encoding.ASCII.GetBytes("\n% appended after signing\n")]);

        var appended = await host.InvokeAsync(new VerifyPdfSignatureTool(), new { pdf = "appended.pdf" });

        Assert.Contains("— MODIFIED AFTER SIGNING", appended, StringComparison.Ordinal);
        Assert.Contains("were added to the file after this signature", appended, StringComparison.Ordinal);

        // A byte changed inside the signed range: the digest no longer matches.
        var tampered = (byte[])signed.Clone();
        var author = Encoding.Latin1.GetString(tampered).IndexOf("Old Author", StringComparison.Ordinal);

        Assert.True(author > 0);
        tampered[author] = (byte)'X';
        await host.UploadAsync("tampered.pdf", tampered);

        var changed = await host.InvokeAsync(new VerifyPdfSignatureTool(), new { pdf = "tampered.pdf" });

        Assert.Contains("— INVALID", changed, StringComparison.Ordinal);
        Assert.Contains("changed after signing", changed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sign_RefusesWithoutAConfiguredCertificateOrOverAnExistingSignature()
    {
        using (var unconfigured = new PdfToolTestHost())
        {
            await unconfigured.UploadAsync("contract.pdf", PdfPropertiesFixtures.TextPdf());

            Assert.Contains("Digital signing is not configured on this host", await unconfigured.InvokeAsync(new SignPdfTool()), StringComparison.Ordinal);
            Assert.Contains("has no digital signatures", await unconfigured.InvokeAsync(new VerifyPdfSignatureTool()), StringComparison.Ordinal);
            Assert.Empty((await unconfigured.LoadWorkspaceAsync()).Documents);
        }

        using var certificate = CreateCertificate();
        using var host = CreateHost(certificate);

        await host.UploadAsync("contract.pdf", PdfPropertiesFixtures.TextPdf());
        await host.InvokeAsync(new SignPdfTool());

        Assert.Contains("already carries 1 digital signature(s)", await host.InvokeAsync(new SignPdfTool()), StringComparison.Ordinal);

        // Any other edit of the signed copy says it breaks the signature.
        Assert.Contains("signatures no longer verify", await host.InvokeAsync(new EditPdfMetadataTool(), new { title = "Changed" }), StringComparison.Ordinal);
    }

    private static PdfToolTestHost CreateHost(X509Certificate2 certificate)
    {
        return new PdfToolTestHost(configure: services =>
        {
            services.AddSingleton<IPdfSigningCertificateProvider>(new TestCertificateProvider(certificate));
            services.AddOptions<PdfSigningOptions>();
        });
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);

        var request = new CertificateRequest("CN=Test Signer, O=Contoso", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, critical: false));

        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pkcs12, "test"), "test", X509KeyStorageFlags.Exportable);
    }

    private sealed class TestCertificateProvider : IPdfSigningCertificateProvider
    {
        private readonly X509Certificate2 _certificate;

        public TestCertificateProvider(X509Certificate2 certificate)
        {
            _certificate = certificate;
        }

        public Task<X509Certificate2> GetCertificateAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_certificate);
        }
    }
}
