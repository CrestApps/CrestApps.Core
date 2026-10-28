namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// Encrypts the strings and streams of a file as <see cref="PdfObjectWriter"/> writes it.
/// </summary>
internal interface IPdfObjectEncryptor
{
    /// <summary>
    /// Gets the lowest PDF version the encryption needs.
    /// </summary>
    double MinimumVersion { get; }

    /// <summary>
    /// Derives the file key, before any object is written.
    /// </summary>
    /// <param name="fileIdentifier">The first part of the file identifier the trailer writes.</param>
    void Prepare(byte[] fileIdentifier);

    /// <summary>
    /// Encrypts a string.
    /// </summary>
    /// <param name="value">The string's bytes.</param>
    /// <param name="objectNumber">The number of the object the string is in.</param>
    /// <returns>The encrypted bytes.</returns>
    byte[] EncryptString(byte[] value, int objectNumber);

    /// <summary>
    /// Encrypts a stream's data.
    /// </summary>
    /// <param name="stream">The stream's dictionary.</param>
    /// <param name="value">The data, as stored.</param>
    /// <param name="objectNumber">The stream's object number.</param>
    /// <returns>The encrypted bytes.</returns>
    byte[] EncryptStream(PdfDictionary stream, byte[] value, int objectNumber);

    /// <summary>
    /// Creates the encryption dictionary the trailer refers to.
    /// </summary>
    /// <returns>The dictionary.</returns>
    PdfDictionary CreateEncryptionDictionary();
}
