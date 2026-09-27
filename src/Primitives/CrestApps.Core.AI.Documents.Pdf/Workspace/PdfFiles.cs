using PdfSharp.Pdf.IO;
using PdfSharpDocument = PdfSharp.Pdf.PdfDocument;
using PigDocument = UglyToad.PdfPig.PdfDocument;
using PigParsingOptions = UglyToad.PdfPig.ParsingOptions;

namespace CrestApps.Core.AI.Documents.Pdf.Workspace;

/// <summary>
/// Opens and saves PDF files with the two libraries the agent uses — PdfPig to read what a page shows, and
/// PDFsharp to change the file — translating their failures into messages the model can act on.
/// </summary>
internal static class PdfFiles
{
    /// <summary>
    /// Opens a PDF for reading its content.
    /// </summary>
    /// <param name="bytes">The file.</param>
    /// <param name="password">The password, when the file needs one to open.</param>
    /// <returns>The document. The caller disposes it.</returns>
    public static PigDocument OpenForReading(byte[] bytes, string password = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        try
        {
            return string.IsNullOrEmpty(password)
                ? PigDocument.Open(bytes)
                : PigDocument.Open(bytes, new PigParsingOptions { Password = password });
        }
        catch (Exception ex) when (IsPasswordFailure(ex))
        {
            throw new PdfToolException("This PDF is protected with a password. Ask the user for it and pass it as 'password'.", ex);
        }
        catch (Exception ex) when (ex is not PdfToolException and not OperationCanceledException)
        {
            throw new PdfToolException($"The file could not be read as a PDF: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Opens a PDF for changing it.
    /// </summary>
    /// <param name="bytes">The file.</param>
    /// <param name="password">The owner password, when the file is protected against changes.</param>
    /// <returns>The document. The caller disposes it.</returns>
    public static PdfSharpDocument OpenForEditing(byte[] bytes, string password = null)
    {
        return Open(bytes, password, PdfDocumentOpenMode.Modify);
    }

    /// <summary>
    /// Opens a PDF to copy its pages into another.
    /// </summary>
    /// <param name="bytes">The file.</param>
    /// <param name="password">The password, when the file needs one.</param>
    /// <returns>The document. The caller disposes it.</returns>
    public static PdfSharpDocument OpenForImport(byte[] bytes, string password = null)
    {
        return Open(bytes, password, PdfDocumentOpenMode.Import);
    }

    /// <summary>
    /// Saves a PDF to bytes.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The file.</returns>
    public static byte[] Save(PdfSharpDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        using var buffer = new MemoryStream();
        document.Save(buffer, closeStream: false);

        return buffer.ToArray();
    }

    /// <summary>
    /// Counts the pages of a PDF without keeping it open.
    /// </summary>
    /// <param name="bytes">The file.</param>
    /// <returns>The page count, or 0 when the file cannot be read.</returns>
    public static int CountPages(byte[] bytes)
    {
        try
        {
            using var document = PigDocument.Open(bytes);

            return document.NumberOfPages;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static PdfSharpDocument Open(byte[] bytes, string password, PdfDocumentOpenMode mode)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var stream = new MemoryStream(bytes, writable: false);

        try
        {
            return string.IsNullOrEmpty(password)
                ? PdfReader.Open(stream, mode)
                : PdfReader.Open(stream, password, mode);
        }
        catch (Exception ex) when (IsPasswordFailure(ex))
        {
            stream.Dispose();

            throw new PdfToolException(
                string.IsNullOrEmpty(password)
                    ? "This PDF is protected and cannot be changed without its owner password. Ask the user for it and pass it as 'password', or use remove_pdf_security first."
                    : "The password given does not open this PDF for changes. It needs the owner password, not only the one that opens it for reading.",
                ex);
        }
        catch (Exception ex) when (ex is not PdfToolException and not OperationCanceledException)
        {
            stream.Dispose();

            throw new PdfToolException($"The file could not be opened as a PDF: {ex.Message}", ex);
        }
    }

    private static bool IsPasswordFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                current.GetType().Name.Contains("Password", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
