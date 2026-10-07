using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PdfSharp.Drawing;
using PdfSharp.Pdf.Signatures;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Signs a PDF digitally with the signing identity the host configured.
/// </summary>
internal sealed class SignPdfTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.SignPdf;

    private const double DefaultWidth = 200;
    private const double DefaultHeight = 50;
    private const double DefaultMargin = 36;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Password}},
            {{PdfToolSchemas.SaveAs}},
            "reason": { "type": "string", "description": "Why the document is signed, for example \"Approved\"." },
            "location": { "type": "string", "description": "Where it is signed, for example \"Toronto\"." },
            "page": { "type": "integer", "description": "One-based page for a visible signature box. Omit for an invisible signature." },
            "x": { "type": "number", "description": "Left edge of the visible box, in points from the left of the page. Defaults to the bottom-right corner." },
            "y": { "type": "number", "description": "Top edge of the visible box, in points from the top of the page." },
            "width": { "type": "number", "description": "Width of the visible box, in points. Defaults to 200." },
            "height": { "type": "number", "description": "Height of the visible box, in points. Defaults to 50." }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignPdfTool"/> class.
    /// </summary>
    public SignPdfTool()
        : base(Schema)
    {
    }

    /// <summary>
    /// Gets the name.
    /// </summary>
    public override string Name => TheName;

    /// <summary>
    /// Gets the description.
    /// </summary>
    public override string Description => "Digitally signs a PDF with the signing certificate the host configured (never one from the conversation), invisibly or in a visible box on a page, and saves a signed working copy. Any later change breaks the signature, so sign as the very last step and then export. A PDF that already carries signatures cannot be signed again here.";

    /// <summary>
    /// Signs the PDF.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var provider = arguments.Services?.GetService<IPdfSigningCertificateProvider>();
        var certificate = provider is null
            ? null
            : await provider.GetCertificateAsync(cancellationToken);

        if (certificate is null)
        {
            throw new PdfToolException("Digital signing is not configured on this host. An administrator can enable it with AddPdfSigning(...) and a signing certificate. The PDF was not changed.");
        }

        if (!certificate.HasPrivateKey)
        {
            throw new PdfToolException("The signing certificate this host configured has no private key, so it cannot sign. An administrator needs to configure a certificate with its private key.");
        }

        var now = context.TimeProvider.GetUtcNow();

        if (now.UtcDateTime < certificate.NotBefore.ToUniversalTime() || now.UtcDateTime > certificate.NotAfter.ToUniversalTime())
        {
            throw new PdfToolException(string.Create(
                CultureInfo.InvariantCulture,
                $"The signing certificate this host configured is not valid now (valid {certificate.NotBefore.ToUniversalTime():yyyy-MM-dd} to {certificate.NotAfter.ToUniversalTime():yyyy-MM-dd}); an administrator needs to renew it."));
        }

        var options = arguments.Services.GetService<IOptions<PdfSigningOptions>>()?.Value ?? new PdfSigningOptions();
        var (digest, digestName) = ReadDigest(options.DigestAlgorithm);
        var authority = ReadAuthority(options.TimestampAuthorityUrl);
        var password = arguments.GetString("password");

        return await context.MutateAsync(async state =>
        {
            var target = context.FindPdf(state, arguments.Pdf());
            var bytes = await context.ReadPdfAsync(target, cancellationToken);
            var wasProtected = PdfProtection.IsEncrypted(bytes, password);

            using var pig = PdfFiles.OpenForReading(bytes, password);
            using var document = PdfFiles.OpenForEditing(bytes, password);

            var existing = PdfObjects.CountSignatures(document);

            if (existing > 0)
            {
                throw new PdfToolException($"{PdfPropertiesToolText.Capitalize(target.Describe())} already carries {existing} digital signature(s). Signing here rewrites the whole file, which would break them; check them with verify_pdf_signature instead. The PDF was not changed.");
            }

            var (pageIndex, rectangle, placement) = ReadPlacement(arguments, pig);
            var sync = PdfMetadataSync.Capture(document);

            PdfFontConfiguration.Ensure();

            DigitalSignatureHandler.ForDocument(
                document,
                new PdfCmsSigner(certificate, digest, authority, now, () => CreateClient(arguments.Services)),
                new DigitalSignatureOptions
                {
                    Reason = arguments.GetString("reason") ?? options.DefaultReason ?? string.Empty,
                    Location = arguments.GetString("location") ?? options.DefaultLocation ?? string.Empty,
                    ContactInfo = options.ContactInfo ?? string.Empty,
                    AppName = "CrestApps",
                    Rectangle = rectangle,
                    PageIndex = pageIndex,
                });

            document.Info.ModificationDate = now.UtcDateTime;

            byte[] signed;

            using (var buffer = new MemoryStream())
            {
                // The signature is computed while the file is written; a time stamp is fetched then too.
                await document.SaveAsync(buffer, closeStream: false);
                signed = buffer.ToArray();
            }

            using var check = PdfFiles.OpenForReading(signed);

            var report = PdfSignatureVerifier.Verify(signed, check, now, out _).FirstOrDefault();

            if (report is null || report.Status != PdfSignatureReport.Valid)
            {
                throw new PdfToolException("The signed file did not verify, so it was not kept: " + string.Join(" ", report?.Findings ?? ["no signature was found in it."]));
            }

            var working = await context.SaveWorkingFileAsync(state, target, arguments.GetString("save_as"), signed, "Signed digitally by " + certificate.GetNameInfo(X509NameType.SimpleName, false), cancellationToken);
            var answer = new StringBuilder();

            answer.AppendLine("Signed. " + PdfPropertiesToolText.Saved(target, working));
            answer.AppendLine($"Signer: {certificate.Subject}, issued by {certificate.Issuer}, certificate valid until {certificate.NotAfter.ToUniversalTime():yyyy-MM-dd}.");
            answer.AppendLine($"Signature: {placement}; digest {digestName}; " + (authority is null
                ? "signing time from this server's clock (no time-stamp authority is configured)."
                : "time-stamped by the configured authority."));

            if (report.Trusted == false)
            {
                answer.AppendLine("The certificate is not trusted on this server (" + report.TrustDetail + "), so viewers will show the signer as unverified unless they trust that certificate.");
            }

            answer.AppendLine("The signature covers the file exactly as it is now: any later change — including edits with other PDF tools — breaks it. Sign as the last step and give the user this copy with export_pdf.");

            PdfPropertiesToolText.AppendNotes(
                answer,
                wasProtected ? "The source was password protected; the signed copy is saved without that protection, and protecting it now would break the signature." : null,
                sync.Conformance.Count > 0 ? $"The file claimed {string.Join(" and ", sync.Conformance)}; signing rewrote its metadata without that identification." : null);

            return answer.ToString().TrimEnd();
        }, cancellationToken);
    }

    private static (HashAlgorithmName Digest, string Name) ReadDigest(string value)
    {
        return (value ?? "SHA256").Trim().Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant() switch
        {
            "SHA384" => (HashAlgorithmName.SHA384, "SHA-384"),
            "SHA512" => (HashAlgorithmName.SHA512, "SHA-512"),
            _ => (HashAlgorithmName.SHA256, "SHA-256"),
        };
    }

    private static HttpClient CreateClient(IServiceProvider services)
    {
        var client = services?.GetService<IHttpClientFactory>()?.CreateClient(nameof(SignPdfTool)) ?? new HttpClient();

        client.Timeout = TimeSpan.FromSeconds(30);

        return client;
    }

    private static Uri ReadAuthority(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri
            : throw new PdfToolException("The time-stamp authority address this host configured is not an http or https address; an administrator needs to correct it.");
    }

    private static (int PageIndex, XRect Rectangle, string Description) ReadPlacement(PdfToolArguments arguments, PigDocument pig)
    {
        var page = arguments.GetInt("page");
        var x = arguments.GetDouble("x");
        var y = arguments.GetDouble("y");
        var width = arguments.GetDouble("width");
        var height = arguments.GetDouble("height");

        if (page is null)
        {
            if (x.HasValue || y.HasValue || width.HasValue || height.HasValue)
            {
                throw new PdfToolException("A visible signature box needs 'page' as well as its position.");
            }

            return (0, default, "invisible (no box on the page; viewers list it in their signatures panel)");
        }

        if (page < 1 || page > pig.NumberOfPages)
        {
            throw new PdfToolException($"Page {page} does not exist; the document has {pig.NumberOfPages} page(s).");
        }

        var pigPage = pig.GetPage(page.Value);
        var visible = PdfBox.VisibleArea(pigPage);
        var boxWidth = width ?? DefaultWidth;
        var boxHeight = height ?? DefaultHeight;

        if (boxWidth < 20 || boxHeight < 10)
        {
            throw new PdfToolException("A visible signature box needs to be at least 20 points wide and 10 points high.");
        }

        var left = x ?? Math.Max(0, visible.Width - DefaultMargin - boxWidth);
        var top = y ?? Math.Max(0, visible.Height - DefaultMargin - boxHeight);
        var box = PdfBox.FromTopLeft(pigPage, left, top, boxWidth, boxHeight);

        if (box.Left < visible.Left - 0.5 || box.Bottom < visible.Bottom - 0.5 || box.Right > visible.Right + 0.5 || box.Top > visible.Top + 0.5)
        {
            throw new PdfToolException(string.Create(CultureInfo.InvariantCulture, $"That box does not fit on page {page}, which is {visible.Width:0} by {visible.Height:0} points."));
        }

        return (page.Value - 1, new XRect(box.Left, box.Bottom, box.Width, box.Height), string.Create(CultureInfo.InvariantCulture, $"visible on page {page} at {box.Describe(pigPage)}"));
    }
}
