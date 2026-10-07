using System.Formats.Asn1;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using PdfSharp.Pdf.Signatures;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// Makes the PKCS#7 signature PDFsharp embeds when it signs a document, with the signed attributes a
/// validator expects.
/// </summary>
/// <remarks>
/// PDFsharp's own signer signs the content directly, with no signed attributes: there is no signed message
/// digest, the signing time goes into the unsigned attributes where anyone could change it, and nothing
/// binds the signer's certificate. This signer signs the message digest, the signing time and the
/// certificate's hash (the ESS signing-certificate-v2 attribute), and adds an RFC 3161 time stamp when an
/// authority is configured.
/// </remarks>
internal sealed class PdfCmsSigner : IDigitalSigner
{
    private const string SigningCertificateV2Oid = "1.2.840.113549.1.9.16.2.47";
    private const string TimestampTokenOid = "1.2.840.113549.1.9.16.2.14";

    private readonly X509Certificate2 _certificate;
    private readonly HashAlgorithmName _digest;
    private readonly Uri _timestampAuthority;
    private readonly DateTimeOffset _signingTime;
    private readonly Func<HttpClient> _createClient;
    private int _size;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfCmsSigner"/> class.
    /// </summary>
    /// <param name="certificate">The signing certificate, with its private key.</param>
    /// <param name="digest">The digest algorithm: SHA-256, SHA-384 or SHA-512.</param>
    /// <param name="timestampAuthority">The RFC 3161 time-stamp authority, or <see langword="null"/>.</param>
    /// <param name="signingTime">The signing time recorded in the signature.</param>
    /// <param name="createClient">Creates the HTTP client the time-stamp authority is called with.</param>
    public PdfCmsSigner(
        X509Certificate2 certificate,
        HashAlgorithmName digest,
        Uri timestampAuthority,
        DateTimeOffset signingTime,
        Func<HttpClient> createClient)
    {
        _certificate = certificate;
        _digest = digest;
        _timestampAuthority = timestampAuthority;
        _signingTime = signingTime;
        _createClient = createClient;
    }

    /// <summary>
    /// Gets the signer's name, as the signature appearance shows it.
    /// </summary>
    public string CertificateName => _certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false) ?? "(unnamed)";

    /// <summary>
    /// Returns the room the signature needs in the file, by making one over a byte.
    /// </summary>
    /// <returns>The size, in bytes, with room to spare.</returns>
    public async Task<int> GetSignatureSizeAsync()
    {
        if (_size == 0)
        {
            using var probe = new MemoryStream([0]);

            // A time stamp's size varies a little from one response to the next.
            _size = (await GetSignatureAsync(probe)).Length + (_timestampAuthority is null ? 16 : 256);
        }

        return _size;
    }

    /// <summary>
    /// Signs the bytes a signature covers.
    /// </summary>
    /// <param name="stream">The covered bytes.</param>
    /// <returns>The DER-encoded PKCS#7 signature.</returns>
    public async Task<byte[]> GetSignatureAsync(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // PDFsharp hands over a stream of the covered ranges that can be rewound but not otherwise sought.
        var content = new byte[stream.Length];

        stream.Position = 0;
        await stream.ReadExactlyAsync(content);

        var cms = new SignedCms(new ContentInfo(content), detached: true);
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, _certificate)
        {
            DigestAlgorithm = DigestOid(_digest),
            IncludeOption = X509IncludeOption.ExcludeRoot,
        };

        // Any signed attribute makes the signer sign the content type and the message digest too.
        signer.SignedAttributes.Add(new Pkcs9SigningTime(_signingTime.UtcDateTime));
        signer.SignedAttributes.Add(new AsnEncodedData(SigningCertificateV2Oid, EncodeSigningCertificate(_certificate)));
        cms.ComputeSignature(signer, silent: true);

        if (_timestampAuthority is not null)
        {
            await AddTimestampAsync(cms.SignerInfos[0]);
        }

        return cms.Encode();
    }

    private async Task AddTimestampAsync(SignerInfo signerInfo)
    {
        var request = Rfc3161TimestampRequest.CreateFromSignerInfo(signerInfo, _digest, requestSignerCertificates: true, nonce: RandomNumberGenerator.GetBytes(8));

        using var body = new ReadOnlyMemoryContent(request.Encode());

        body.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-query");

        using var client = _createClient();
        using var response = await client.PostAsync(_timestampAuthority, body);

        if (!response.IsSuccessStatusCode)
        {
            throw new CryptographicException($"The time-stamp authority answered {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        var token = request.ProcessResponse(await response.Content.ReadAsByteArrayAsync(), out _);

        signerInfo.AddUnsignedAttribute(new AsnEncodedData(TimestampTokenOid, token.AsSignedCms().Encode()));
    }

    private static byte[] EncodeSigningCertificate(X509Certificate2 certificate)
    {
        // SigningCertificateV2 ::= SEQUENCE { certs SEQUENCE OF ESSCertIDv2 }, with the SHA-256 hash that is
        // the default algorithm of an ESSCertIDv2 and so is not written.
        var writer = new AsnWriter(AsnEncodingRules.DER);

        using (writer.PushSequence())
        {
            using (writer.PushSequence())
            {
                using (writer.PushSequence())
                {
                    writer.WriteOctetString(SHA256.HashData(certificate.RawData));
                }
            }
        }

        return writer.Encode();
    }

    private static Oid DigestOid(HashAlgorithmName digest)
    {
        if (digest == HashAlgorithmName.SHA384)
        {
            return new Oid("2.16.840.1.101.3.4.2.2");
        }

        return digest == HashAlgorithmName.SHA512
            ? new Oid("2.16.840.1.101.3.4.2.3")
            : new Oid("2.16.840.1.101.3.4.2.1");
    }
}
