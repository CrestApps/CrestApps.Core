using CrestApps.Core.AI.Documents.Pdf.Workspace;
using PdfSharp.Pdf.IO;
using PdfSharpDocument = PdfSharp.Pdf.PdfDocument;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// A PDF opened for checking with both libraries the agent uses: PdfPig for what the pages show, and PDFsharp
/// for the objects the file is made of.
/// </summary>
/// <remarks>
/// A damaged file may open with one library and not the other — PdfPig repairs what it can, PDFsharp does
/// not — so each is optional and the reason it could not open is kept for the report.
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
    /// Gets the document as PDFsharp reads it, or <see langword="null"/> when it could not be opened.
    /// </summary>
    public PdfSharpDocument Objects { get; private set; }

    /// <summary>
    /// Gets why PdfPig could not open the file, or <see langword="null"/>.
    /// </summary>
    public string ContentError { get; private set; }

    /// <summary>
    /// Gets why PDFsharp could not open the file, or <see langword="null"/>.
    /// </summary>
    public string ObjectsError { get; private set; }

    /// <summary>
    /// Gets the problems PDFsharp reported while reading the file.
    /// </summary>
    public List<string> ReaderProblems { get; } = [];

    /// <summary>
    /// Gets the number of pages, as far as either library could tell.
    /// </summary>
    public int PageCount => Content?.NumberOfPages ?? Objects?.PageCount ?? 0;

    /// <summary>
    /// Opens a PDF for checking.
    /// </summary>
    /// <param name="bytes">The file.</param>
    /// <param name="password">The password, when the file needs one.</param>
    /// <param name="requireContent">Whether the call fails when PdfPig cannot open the file.</param>
    /// <param name="requireObjects">Whether the call fails when PDFsharp cannot open the file.</param>
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
            document.Objects = OpenObjects(bytes, password, document.ReaderProblems);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (IsPasswordFailure(ex))
            {
                document.Dispose();

                throw new PdfToolException("This PDF is protected with a password. Ask the user for it and pass it as 'password'.", ex);
            }

            document.ObjectsError = ex.Message;

            if (requireObjects)
            {
                document.Dispose();

                throw new PdfToolException($"The objects this PDF is made of could not be read ({ex.Message}), so these checks cannot run. validate_pdf reports what is damaged.", ex);
            }
        }

        return document;
    }

    /// <summary>
    /// Releases both documents.
    /// </summary>
    public void Dispose()
    {
        Content?.Dispose();
        Objects?.Dispose();
    }

    private static PdfSharpDocument OpenObjects(byte[] bytes, string password, List<string> problems)
    {
        var options = new PdfReaderOptions
        {
            ReaderProblemCallback = details => problems.Add(string.IsNullOrWhiteSpace(details.Description)
                ? details.Title
                : details.Title + ": " + details.Description),
        };

        var stream = new MemoryStream(bytes, writable: false);

        try
        {
            return string.IsNullOrEmpty(password)
                ? PdfReader.Open(stream, PdfDocumentOpenMode.Import, options)
                : PdfReader.Open(stream, password, PdfDocumentOpenMode.Import, options);
        }
        catch
        {
            stream.Dispose();

            throw;
        }
    }

    private static bool IsPasswordMessage(string message)
    {
        return message.Contains("password", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPasswordFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (IsPasswordMessage(current.Message) || current.GetType().Name.Contains("Password", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
