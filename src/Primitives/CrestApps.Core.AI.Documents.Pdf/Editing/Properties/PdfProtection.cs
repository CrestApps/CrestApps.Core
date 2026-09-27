using CrestApps.Core.AI.Documents.Pdf.Workspace;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// Tells whether a PDF is encrypted, as the file says rather than as PDFsharp reports it.
/// </summary>
/// <remarks>
/// PDFsharp decrypts a document while opening it and drops its encryption, so an opened document always
/// reports that it is not encrypted, and saving it writes an unprotected file. The file itself is asked
/// instead, through PdfPig.
/// </remarks>
internal static class PdfProtection
{
    /// <summary>
    /// The note an answer carries when an edit saved a protected source without its protection.
    /// </summary>
    public const string DroppedNote = "The source was password protected; like every edit, this working copy is saved without that protection. Protect it again with protect_pdf as the last step if it must stay protected.";

    /// <summary>
    /// Returns whether a file declares encryption, without parsing it: an encrypted file names its
    /// encryption dictionary in its trailer, which is never itself encrypted.
    /// </summary>
    /// <param name="bytes">The file.</param>
    /// <returns><see langword="true"/> when the file names an encryption dictionary.</returns>
    public static bool DeclaresEncryption(ReadOnlySpan<byte> bytes)
    {
        return bytes.IndexOf("/Encrypt"u8) >= 0;
    }
    /// <summary>
    /// Returns whether a file is encrypted.
    /// </summary>
    /// <param name="bytes">The file.</param>
    /// <param name="password">The password that opens it, when it needs one.</param>
    /// <returns><see langword="true"/> when the file is encrypted.</returns>
    public static bool IsEncrypted(byte[] bytes, string password)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        try
        {
            using var document = PdfFiles.OpenForReading(bytes, password);

            return document.IsEncrypted;
        }
        catch (PdfToolException)
        {
            // A file that cannot be read without a password is encrypted.
            return true;
        }
    }

    /// <summary>
    /// Describes the protection an edit drops, for an answer.
    /// </summary>
    /// <param name="bytes">The file that was edited.</param>
    /// <param name="password">The password the call opened it with.</param>
    /// <returns>A sentence, or <see langword="null"/> when the file was not protected.</returns>
    public static string DescribeDropped(byte[] bytes, string password)
    {
        if (string.IsNullOrEmpty(password) || !IsEncrypted(bytes, password))
        {
            return null;
        }

        return DroppedNote;
    }
}
