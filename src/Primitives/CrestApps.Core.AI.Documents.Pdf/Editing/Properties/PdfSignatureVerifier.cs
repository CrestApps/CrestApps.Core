using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using CrestApps.Core.AI.Documents.Pdf.Rendering;
using UglyToad.PdfPig.AcroForms.Fields;
using UglyToad.PdfPig.Tokens;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// Verifies the digital signatures of a PDF against the bytes of the file.
/// </summary>
/// <remarks>
/// A signature covers the byte ranges its dictionary lists — the whole file as it was when signed, except
/// the <c>/Contents</c> string that holds the signature. Verifying it means hashing exactly those bytes,
/// checking the PKCS#7 signature and its signed message digest against that hash, and checking that the
/// ranges end where the file ends: bytes after them were added once the signature was made.
/// </remarks>
internal static class PdfSignatureVerifier
{
    private const string MessageDigestOid = "1.2.840.113549.1.9.4";
    private const string SigningTimeOid = "1.2.840.113549.1.9.5";
    private const string TimestampTokenOid = "1.2.840.113549.1.9.16.2.14";

    /// <summary>
    /// Verifies every signature of a document.
    /// </summary>
    /// <param name="file">The file's bytes.</param>
    /// <param name="document">The same file, opened with PdfPig.</param>
    /// <param name="now">The current time, used when a signature records none.</param>
    /// <param name="unsignedFields">The names of signature fields that carry no signature.</param>
    /// <returns>One report per signature, in form order.</returns>
    public static List<PdfSignatureReport> Verify(byte[] file, PigDocument document, DateTimeOffset now, out List<string> unsignedFields)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(document);

        unsignedFields = [];

        var reports = new List<PdfSignatureReport>();

        foreach (var field in SignatureFields(document))
        {
            var signature = PdfPigTokens.GetDictionary(document, field.Dictionary, "V");
            var name = field.Information?.PartialName ?? "(unnamed)";

            if (signature is null)
            {
                unsignedFields.Add(name);

                continue;
            }

            var report = new PdfSignatureReport
            {
                FieldName = name,
                Page = field.PageNumber,
                Visible = field.Bounds is { } bounds && bounds.Width > 1 && bounds.Height > 1,
                SubFilter = PdfPigTokens.GetText(document, signature, "SubFilter"),
                SignerName = PdfPigTokens.GetText(document, signature, "Name"),
                Reason = PdfPigTokens.GetText(document, signature, "Reason"),
                Location = PdfPigTokens.GetText(document, signature, "Location"),
            };

            var recorded = PdfXmpPacket.ToXmpDate(PdfPigTokens.GetText(document, signature, "M"));
            var fallbackTime = DateTimeOffset.TryParse(recorded, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? (parsed, "as recorded in the signature dictionary, not verified")
                : (now, "the current time, because the signature records none");

            Verify(file, PdfPigTokens.GetNumbers(document, signature, "ByteRange"), fallbackTime, report);
            reports.Add(report);
        }

        return reports;
    }

    private static void Verify(byte[] file, List<double> byteRange, (DateTimeOffset Time, string Source) fallbackTime, PdfSignatureReport report)
    {
        report.Status = PdfSignatureReport.Invalid;

        if (!TryReadRanges(byteRange, file.LongLength, out var ranges))
        {
            report.Findings.Add("Its /ByteRange is missing or points outside the file, so what it signs cannot be determined.");

            return;
        }

        var contents = ReadContents(file, ranges);

        if (contents is null)
        {
            report.Findings.Add("The gap in its /ByteRange does not hold exactly its /Contents, so the signed ranges were tampered with.");

            return;
        }

        var signed = Concatenate(file, ranges);
        var (lastStart, lastLength) = ranges[^1];
        var end = lastStart + lastLength;

        report.BytesAfter = file.LongLength - end;
        report.CoversWholeFile = report.BytesAfter == 0 || file.AsSpan((int)end).Trim(" \r\n\t\0"u8).Length == 0;

        var valid = string.Equals(report.SubFilter, "ETSI.RFC3161", StringComparison.Ordinal)
            ? VerifyTimestamp(contents, signed, report)
            : VerifyCms(contents, signed, fallbackTime, report);

        if (!valid)
        {
            return;
        }

        report.Status = report.CoversWholeFile
            ? PdfSignatureReport.Valid
            : PdfSignatureReport.ModifiedAfterSigning;

        if (!report.CoversWholeFile)
        {
            report.Findings.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{report.BytesAfter:N0} bytes were added to the file after this signature was made (a later revision: edits, annotations, form filling or another signature). The revision it signed is intact, but it does not vouch for those changes."));
        }

        if (report.Trusted == false)
        {
            report.Findings.Add("The signature is intact, but its certificate is not trusted on this server: " + report.TrustDetail);
        }
    }

    private static bool VerifyCms(byte[] contents, byte[] signed, (DateTimeOffset Time, string Source) fallbackTime, PdfSignatureReport report)
    {
        var encapsulated = string.Equals(report.SubFilter, "adbe.pkcs7.sha1", StringComparison.Ordinal);
        SignedCms cms;

        try
        {
            cms = encapsulated
                ? new SignedCms()
                : new SignedCms(new ContentInfo(signed), detached: true);

            cms.Decode(contents);
        }
        catch (CryptographicException ex)
        {
            report.Findings.Add("Its /Contents is not a readable PKCS#7 signature: " + ex.Message);

            return false;
        }

        if (cms.SignerInfos.Count == 0)
        {
            report.Findings.Add("Its PKCS#7 data holds no signer.");

            return false;
        }

        var signer = cms.SignerInfos[0];
        var hashName = HashNameOf(signer.DigestAlgorithm.Value);

        report.DigestAlgorithm = hashName?.Name ?? signer.DigestAlgorithm.FriendlyName ?? signer.DigestAlgorithm.Value;

        var (time, source) = ReadSigningTime(signer) ?? fallbackTime;

        report.SigningTime = ToIso(time, source);
        DescribeCertificate(signer.Certificate, cms.Certificates, time, report);

        var valid = true;
        var signedDigest = ReadMessageDigest(signer);

        if (encapsulated)
        {
            if (!Hash(HashAlgorithmName.SHA1, signed).AsSpan().SequenceEqual(cms.ContentInfo.Content))
            {
                report.Findings.Add("The document bytes it covers were changed after signing: their SHA-1 digest does not match the signed one.");
                valid = false;
            }
        }
        else if (hashName is { } name && signedDigest is not null)
        {
            if (!Hash(name, signed).AsSpan().SequenceEqual(signedDigest))
            {
                report.Findings.Add("The document bytes it covers were changed after signing: their digest does not match the signed message digest.");
                valid = false;
            }
        }

        try
        {
            cms.CheckSignature(verifySignatureOnly: true);
        }
        catch (CryptographicException ex)
        {
            if (valid)
            {
                // Without a signed message digest the signature is over the content itself, so a failure
                // cannot tell a changed document from a damaged signature.
                report.Findings.Add(signedDigest is null && !encapsulated
                    ? "The signature does not match the bytes it covers: the document was changed after signing, or the signature is damaged."
                    : "The signature value does not verify: " + ex.Message);
            }

            valid = false;
        }

        return valid;
    }

    private static bool VerifyTimestamp(byte[] contents, byte[] signed, PdfSignatureReport report)
    {
        if (!Rfc3161TimestampToken.TryDecode(contents, out var token, out _))
        {
            report.Findings.Add("Its /Contents is not a readable RFC 3161 time stamp.");

            return false;
        }

        var time = token.TokenInfo.Timestamp;

        report.DigestAlgorithm = HashNameOf(token.TokenInfo.HashAlgorithmId.Value)?.Name ?? token.TokenInfo.HashAlgorithmId.Value;
        report.SigningTime = ToIso(time, "from the document time stamp");

        if (!token.VerifySignatureForData(signed, out var authority))
        {
            report.Findings.Add("The document time stamp does not match the bytes it covers: the document was changed after it was stamped.");

            return false;
        }

        DescribeCertificate(authority, null, time, report);

        return true;
    }

    private static void DescribeCertificate(X509Certificate2 certificate, X509Certificate2Collection extra, DateTimeOffset signedAt, PdfSignatureReport report)
    {
        if (certificate is null)
        {
            report.Findings.Add("The signature does not embed the signer's certificate, so the signer cannot be identified.");

            return;
        }

        report.CertificateSubject = certificate.Subject;
        report.CertificateIssuer = certificate.Issuer;
        report.CertificateSerialNumber = certificate.SerialNumber;
        report.CertificateThumbprint = certificate.Thumbprint;
        report.CertificateValidity = string.Create(
            CultureInfo.InvariantCulture,
            $"{certificate.NotBefore.ToUniversalTime():yyyy-MM-dd} to {certificate.NotAfter.ToUniversalTime():yyyy-MM-dd}");

        if (signedAt.UtcDateTime < certificate.NotBefore.ToUniversalTime() || signedAt.UtcDateTime > certificate.NotAfter.ToUniversalTime())
        {
            report.Findings.Add("The certificate was not valid at the signing time.");
        }

        using var chain = new X509Chain();

        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(1);
        chain.ChainPolicy.VerificationTime = signedAt.LocalDateTime;

        if (extra is not null)
        {
            chain.ChainPolicy.ExtraStore.AddRange(extra);
        }

        try
        {
            report.Trusted = chain.Build(certificate);

            if (report.Trusted == false)
            {
                report.TrustDetail = string.Join(
                    "; ",
                    chain.ChainStatus.Select(status => DescribeStatus(status.Status)).Distinct(StringComparer.Ordinal));
            }
        }
        catch (CryptographicException ex)
        {
            report.Trusted = false;
            report.TrustDetail = "the certificate chain could not be built: " + ex.Message;
        }
    }

    private static string DescribeStatus(X509ChainStatusFlags status)
    {
        return status switch
        {
            X509ChainStatusFlags.UntrustedRoot => "it chains to a root this server does not trust (for example a self-signed or private certificate)",
            X509ChainStatusFlags.PartialChain => "the certificates that issued it are not available",
            X509ChainStatusFlags.NotTimeValid => "a certificate in its chain is expired or not yet valid",
            X509ChainStatusFlags.Revoked => "a certificate in its chain was revoked",
            X509ChainStatusFlags.NotSignatureValid => "a certificate in its chain has an invalid signature",
            X509ChainStatusFlags.NotValidForUsage => "the certificate is not valid for signing",
            _ => status.ToString(),
        };
    }

    private static (DateTimeOffset Time, string Source)? ReadSigningTime(SignerInfo signer)
    {
        foreach (var attribute in signer.UnsignedAttributes)
        {
            if (attribute.Oid?.Value != TimestampTokenOid || attribute.Values.Count == 0)
            {
                continue;
            }

            if (Rfc3161TimestampToken.TryDecode(attribute.Values[0].RawData, out var token, out _))
            {
                var verified = token.VerifySignatureForSignerInfo(signer, out _);

                return (token.TokenInfo.Timestamp, verified ? "from a verified time stamp" : "from a time stamp that does not verify");
            }
        }

        foreach (var (attributes, source) in new[] { (signer.SignedAttributes, "signed by the signer"), (signer.UnsignedAttributes, "unsigned, as the signer's software recorded it") })
        {
            foreach (var attribute in attributes)
            {
                if (attribute.Oid?.Value != SigningTimeOid || attribute.Values.Count == 0)
                {
                    continue;
                }

                var signingTime = new Pkcs9SigningTime();

                signingTime.CopyFrom(attribute.Values[0]);

                return (new DateTimeOffset(signingTime.SigningTime.ToUniversalTime(), TimeSpan.Zero), source);
            }
        }

        return null;
    }

    private static byte[] ReadMessageDigest(SignerInfo signer)
    {
        foreach (var attribute in signer.SignedAttributes)
        {
            if (attribute.Oid?.Value != MessageDigestOid || attribute.Values.Count == 0)
            {
                continue;
            }

            var digest = new Pkcs9MessageDigest();

            digest.CopyFrom(attribute.Values[0]);

            return digest.MessageDigest;
        }

        return null;
    }

    private static bool TryReadRanges(List<double> numbers, long fileLength, out List<(long Start, long Length)> ranges)
    {
        ranges = [];

        if (numbers is null || numbers.Count < 4 || numbers.Count % 2 != 0)
        {
            return false;
        }

        var previousEnd = -1L;

        for (var index = 0; index < numbers.Count; index += 2)
        {
            var start = (long)numbers[index];
            var length = (long)numbers[index + 1];

            if (start < 0 || length < 0 || start + length > fileLength || start <= previousEnd)
            {
                return false;
            }

            ranges.Add((start, length));
            previousEnd = start + length;
        }

        return ranges[0].Start == 0;
    }

    private static byte[] ReadContents(byte[] file, List<(long Start, long Length)> ranges)
    {
        var from = ranges[0].Start + ranges[0].Length;
        var to = ranges[1].Start;

        if (to - from < 2 || file[from] != (byte)'<' || file[to - 1] != (byte)'>')
        {
            return null;
        }

        var hex = System.Text.Encoding.ASCII.GetString(file, (int)from + 1, (int)(to - from - 2));

        byte[] bytes;

        try
        {
            bytes = Convert.FromHexString(string.Concat(hex.Where(character => !char.IsWhiteSpace(character))));
        }
        catch (FormatException)
        {
            return null;
        }

        var length = DerLength(bytes);

        return length > 0 && length <= bytes.Length
            ? bytes[..length]
            : bytes;
    }

    private static int DerLength(byte[] bytes)
    {
        if (bytes.Length < 2 || bytes[0] != 0x30)
        {
            return -1;
        }

        if (bytes[1] < 0x80)
        {
            return 2 + bytes[1];
        }

        var count = bytes[1] & 0x7F;

        if (count is 0 or > 4 || bytes.Length < 2 + count)
        {
            return -1;
        }

        var length = 0L;

        for (var index = 0; index < count; index++)
        {
            length = (length << 8) | bytes[2 + index];
        }

        return length > int.MaxValue - 6 ? -1 : 2 + count + (int)length;
    }

    private static byte[] Concatenate(byte[] file, List<(long Start, long Length)> ranges)
    {
        var total = ranges.Sum(range => range.Length);
        var signed = new byte[total];
        var offset = 0L;

        foreach (var (start, length) in ranges)
        {
            Array.Copy(file, start, signed, offset, length);
            offset += length;
        }

        return signed;
    }

    private static HashAlgorithmName? HashNameOf(string oid)
    {
        return oid switch
        {
            "1.3.14.3.2.26" => HashAlgorithmName.SHA1,
            "2.16.840.1.101.3.4.2.1" => HashAlgorithmName.SHA256,
            "2.16.840.1.101.3.4.2.2" => HashAlgorithmName.SHA384,
            "2.16.840.1.101.3.4.2.3" => HashAlgorithmName.SHA512,
            _ => null,
        };
    }

    private static byte[] Hash(HashAlgorithmName name, byte[] data)
    {
        // SHA-1 is only ever used to check a signature someone else made with it, never to make one.
        return CryptographicOperations.HashData(name, data);
    }

    private static string ToIso(DateTimeOffset time, string source)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{time.ToUniversalTime():yyyy-MM-dd HH:mm:ss} UTC ({source})");
    }

    private static List<AcroFieldBase> SignatureFields(PigDocument document)
    {
        var fields = new List<AcroFieldBase>();

        try
        {
            if (!document.TryGetForm(out var form))
            {
                return fields;
            }

            var pending = new Stack<(AcroFieldBase Field, int Depth)>();

            for (var index = form.Fields.Count - 1; index >= 0; index--)
            {
                pending.Push((form.Fields[index], 0));
            }

            while (pending.Count > 0)
            {
                var (field, depth) = pending.Pop();

                if (field is AcroNonTerminalField parent && depth < 32)
                {
                    for (var index = parent.Children.Count - 1; index >= 0; index--)
                    {
                        pending.Push((parent.Children[index], depth + 1));
                    }

                    continue;
                }

                if (field.FieldType == AcroFieldType.Signature || string.Equals(field.RawFieldType, "Sig", StringComparison.Ordinal))
                {
                    fields.Add(field);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A form PdfPig cannot read leaves the signatures that were found so far.
        }

        return fields;
    }
}
