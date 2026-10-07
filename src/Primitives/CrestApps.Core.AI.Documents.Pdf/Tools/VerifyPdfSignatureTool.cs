using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Verifies a PDF's digital signatures and whether the document changed after it was signed.
/// </summary>
internal sealed class VerifyPdfSignatureTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.VerifyPdfSignature;

    private const int MaxSignatures = 20;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Password}}
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="VerifyPdfSignatureTool"/> class.
    /// </summary>
    public VerifyPdfSignatureTool()
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
    public override string Description => "Verifies every digital signature of a PDF against the file's bytes: whether the signature and its digest check out, whether the document was changed or extended after signing, who signed (certificate subject, issuer, validity) and whether that certificate is trusted on this server. Reports each as valid, invalid or modified-after-signing. Reads only; saves nothing.";

    /// <summary>
    /// Verifies the signatures.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var source = await context.FindPdfAsync(arguments.Pdf(), cancellationToken);
        var bytes = await context.ReadPdfAsync(source, cancellationToken);

        using var pig = PdfFiles.OpenForReading(bytes, arguments.GetString("password"));

        var reports = PdfSignatureVerifier.Verify(bytes, pig, context.TimeProvider.GetUtcNow(), out var unsigned);
        var name = PdfPropertiesToolText.Capitalize(source.Describe());

        if (reports.Count == 0)
        {
            return unsigned.Count == 0
                ? $"{name} has no digital signatures."
                : $"{name} has no digital signatures; it has {unsigned.Count} empty signature field(s): {string.Join(", ", unsigned.Take(20).Select(field => $"\"{field}\""))}.";
        }

        var answer = new StringBuilder();
        var valid = reports.Count(report => report.Status == PdfSignatureReport.Valid);
        var invalid = reports.Count(report => report.Status == PdfSignatureReport.Invalid);
        var modified = reports.Count - valid - invalid;

        answer.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{name} has {reports.Count} signature(s): {valid} valid, {invalid} invalid, {modified} modified after signing."));

        var number = 0;

        foreach (var report in reports.Take(MaxSignatures))
        {
            number++;
            answer.AppendLine();
            answer.Append(string.Create(CultureInfo.InvariantCulture, $"Signature {number}: field \"{report.FieldName}\""));
            answer.Append(report.Visible && report.Page is { } page
                ? string.Create(CultureInfo.InvariantCulture, $", visible on page {page}")
                : ", invisible");
            answer.AppendLine(" — " + report.Status.ToUpperInvariant().Replace('-', ' '));

            if (report.CertificateSubject is not null)
            {
                answer.AppendLine($"- Signer: {report.CertificateSubject}; issued by {report.CertificateIssuer}; serial {report.CertificateSerialNumber}; valid {report.CertificateValidity}; SHA-1 thumbprint {report.CertificateThumbprint}.");
            }

            if (report.Trusted is { } trusted)
            {
                answer.AppendLine(trusted
                    ? "- Trust: the certificate chains to a root this server trusts (revocation was not checked)."
                    : "- Trust: not trusted on this server — " + report.TrustDetail + ".");
            }

            var details = new List<string>();

            if (!string.IsNullOrWhiteSpace(report.SignerName))
            {
                details.Add("name " + PdfPropertiesToolText.Quote(report.SignerName, 100));
            }

            if (!string.IsNullOrWhiteSpace(report.Reason))
            {
                details.Add("reason " + PdfPropertiesToolText.Quote(report.Reason, 100));
            }

            if (!string.IsNullOrWhiteSpace(report.Location))
            {
                details.Add("location " + PdfPropertiesToolText.Quote(report.Location, 100));
            }

            if (report.SigningTime is not null)
            {
                answer.AppendLine("- Signed: " + report.SigningTime + (details.Count == 0 ? "." : "; " + string.Join("; ", details) + "."));
            }
            else if (details.Count > 0)
            {
                answer.AppendLine("- Details: " + string.Join("; ", details) + ".");
            }

            answer.AppendLine($"- Format: {report.SubFilter ?? "unknown"}{(report.DigestAlgorithm is null ? string.Empty : ", " + report.DigestAlgorithm)}.");
            answer.AppendLine(report.CoversWholeFile
                ? "- Coverage: the signature covers the whole file."
                : string.Create(CultureInfo.InvariantCulture, $"- Coverage: {report.BytesAfter:N0} bytes follow the signed part of the file."));

            foreach (var finding in report.Findings.Take(6))
            {
                answer.AppendLine("- " + finding);
            }
        }

        if (reports.Count > MaxSignatures)
        {
            answer.AppendLine();
            answer.AppendLine(string.Create(CultureInfo.InvariantCulture, $"({reports.Count - MaxSignatures} more signatures not shown.)"));
        }

        if (unsigned.Count > 0)
        {
            answer.AppendLine();
            answer.AppendLine(string.Create(CultureInfo.InvariantCulture, $"It also has {unsigned.Count} empty signature field(s)."));
        }

        return answer.ToString().TrimEnd();
    }
}
