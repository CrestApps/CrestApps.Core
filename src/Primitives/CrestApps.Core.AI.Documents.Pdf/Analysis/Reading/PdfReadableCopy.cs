using CrestApps.Core.AI.Documents.Pdf.Workspace;
using PdfSharpDocument = PdfSharp.Pdf.PdfDocument;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// Makes the copies of a PDF that readers which take no password, or read every page, can work with: a copy
/// without its password protection, and a copy holding only the pages a call asked about.
/// </summary>
/// <remarks>
/// The ingestion reader and the page renderer open a file without a password, so a protected PDF the user
/// supplied the password for is handed to them decrypted. They also read every page, so a question about
/// three pages of a long report is answered from a copy of those three pages rather than the whole book.
/// </remarks>
internal static class PdfReadableCopy
{
    /// <summary>
    /// The page count below which a document is always read whole: copying pages out costs more than it saves.
    /// </summary>
    public const int SubsetThreshold = 30;

    /// <summary>
    /// Returns the PDF in a form that opens without a password.
    /// </summary>
    /// <param name="bytes">The file.</param>
    /// <param name="password">The password the call supplied, or <see langword="null"/>.</param>
    /// <param name="isEncrypted">Whether the file is encrypted.</param>
    /// <returns>The file itself when it needs no password, otherwise a decrypted copy.</returns>
    public static byte[] WithoutPassword(byte[] bytes, string password, bool isEncrypted)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (string.IsNullOrEmpty(password) || !isEncrypted)
        {
            return bytes;
        }

        try
        {
            using var document = PdfFiles.OpenForEditing(bytes, password);
            document.SecurityHandler.SetEncryptionToNoneAndResetPasswords();

            return PdfFiles.Save(document);
        }
        catch (PdfToolException)
        {
            // Only the password that opens the file for reading was given. Its pages can still be copied out.
        }

        using var source = PdfFiles.OpenForImport(bytes, password);
        using var copy = new PdfSharpDocument();

        foreach (var page in source.Pages)
        {
            copy.AddPage(page);
        }

        return PdfFiles.Save(copy);
    }

    /// <summary>
    /// Returns a copy holding only some pages, when that is worth making.
    /// </summary>
    /// <param name="bytes">The file, readable without a password.</param>
    /// <param name="pages">The one-based pages wanted.</param>
    /// <param name="pageCount">The number of pages the file has.</param>
    /// <returns>The file to read, and the original page number of each of its pages, or <see langword="null"/> when the file is returned whole.</returns>
    public static (byte[] Bytes, List<int> PageMap) Subset(byte[] bytes, IReadOnlyList<int> pages, int pageCount)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(pages);

        if (pageCount <= SubsetThreshold || pages.Count == 0 || pages.Count >= pageCount)
        {
            return (bytes, null);
        }

        try
        {
            using var source = PdfFiles.OpenForImport(bytes);
            using var copy = new PdfSharpDocument();

            var map = new List<int>(pages.Count);

            foreach (var number in pages)
            {
                if (number < 1 || number > source.PageCount)
                {
                    continue;
                }

                copy.AddPage(source.Pages[number - 1]);
                map.Add(number);
            }

            return map.Count == 0
                ? (bytes, null)
                : (PdfFiles.Save(copy), map);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A file PDFsharp cannot copy pages out of is still read, whole.
            return (bytes, null);
        }
    }
}
