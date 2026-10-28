using CrestApps.Core.AI.Documents.Pdf.Workspace;
using ObjectDocument = CrestApps.Core.AI.Documents.Pdf.Objects.PdfDocument;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// A PDF opened for checking twice: as PdfPig reads what the pages show, and as the objects the file is
/// made of.
/// </summary>
/// <remarks>
/// A damaged file may give its pages and not its objects, or the other way round, so each is optional and
/// the reason it could not be read is kept for the report.
/// </remarks>
internal sealed class PdfInspectedDocument : IDisposable
{
    private PdfInspectedDocument(byte[] bytes)
    {
        Bytes = bytes;
    }

    /// <summary>
    /// Gets the file.
    /// </summary>
    public byte[] Bytes { get; }

    /// <summary>
    /// Gets the document as PdfPig reads it, or <see langword="null"/> when it could not be opened.
    /// </summary>
    public PigDocument Content { get; private set; }

    /// <summary>
    /// Gets the objects the document is made of, or <see langword="null"/> when they could not be read.
    /// </summary>
    public ObjectDocument Objects { get; private set; }

    /// <summary>
    /// Gets why PdfPig could not open the file, or <see langword="null"/>.
    /// </summary>
    public string ContentError { get; private set; }

    /// <summary>
    /// Gets why the file's objects could not be read, or <see langword="null"/>.
    /// </summary>
    public string ObjectsError { get; private set; }

    /// <summary>
    /// Gets the problems the reader repaired while reading the file's objects.
    /// </summary>
    public List<string> ReaderProblems { get; } = [];

    /// <summary>
    /// Gets the number of pages, as far as either reading could tell.
    /// </summary>
    public int PageCount => Content?.NumberOfPages ?? Objects?.PageCount ?? 0;

    /// <summary>
    /// Opens a PDF for checking.
    /// </summary>
    /// <param name="bytes">The file.</param>
    /// <param name="password">The password, when the file needs one.</param>
    /// <param name="requireContent">Whether the call fails when PdfPig cannot open the file.</param>
    /// <param name="requireObjects">Whether the call fails when the file's objects cannot be read.</param>
    /// <returns>The document. The caller disposes it.</returns>
    public static PdfInspectedDocument Open(byte[] bytes, string password, bool requireContent, bool requireObjects)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var document = new PdfInspectedDocument(bytes);

        try
        {
            document.Content = PdfFiles.OpenForReading(bytes, password);
        }
        catch (PdfToolException ex) when (!requireContent && !IsPasswordMessage(ex.Message))
        {
            document.ContentError = ex.InnerException?.Message ?? ex.Message;
        }

        try
        {
            document.Objects = PdfFiles.OpenObjects(bytes, password, document.ReaderProblems);
        }
        catch (PdfToolException ex)
        {
            if (IsPasswordMessage(ex.Message))
            {
                document.Dispose();

                throw;
            }

            var reason = ex.InnerException?.Message ?? ex.Message;

            document.ObjectsError = reason;

            if (requireObjects)
            {
                document.Dispose();

                throw new PdfToolException($"The objects this PDF is made of could not be read ({reason}), so these checks cannot run. validate_pdf reports what is damaged.", ex);
            }
        }

        return document;
    }

    /// <summary>
    /// Releases both readings.
    /// </summary>
    public void Dispose()
    {
        Content?.Dispose();
        Objects?.Dispose();
    }

    private static bool IsPasswordMessage(string message)
    {
        return message.Contains("password", StringComparison.OrdinalIgnoreCase);
    }
}
