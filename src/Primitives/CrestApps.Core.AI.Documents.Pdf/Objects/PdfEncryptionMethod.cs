namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// The encryption the standard security handler writes.
/// </summary>
internal enum PdfEncryptionMethod
{
    /// <summary>
    /// AES with a 256-bit key, revision 6. Readers from Acrobat X on open it.
    /// </summary>
    Aes256,

    /// <summary>
    /// AES with a 128-bit key, revision 4.
    /// </summary>
    Aes128,

    /// <summary>
    /// RC4 with a 128-bit key, revision 3, for the oldest readers.
    /// </summary>
    Rc4With128Bits,
}
