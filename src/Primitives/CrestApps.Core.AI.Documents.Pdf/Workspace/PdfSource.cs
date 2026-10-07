using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Documents.Pdf.Workspace;

/// <summary>
/// A PDF a tool call named: an upload, a working copy, or a document being composed.
/// </summary>
internal sealed class PdfSource
{
    private PdfSource(string name, AIDocument upload, PdfWorkingDocument working)
    {
        Name = name;
        Upload = upload;
        Working = working;
    }

    /// <summary>
    /// Gets the name the source is referred to by in answers.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the upload, when the source is one.
    /// </summary>
    public AIDocument Upload { get; }

    /// <summary>
    /// Gets the working document, when the source is one.
    /// </summary>
    public PdfWorkingDocument Working { get; }

    /// <summary>
    /// Gets a value indicating whether the source is an upload, which is never changed.
    /// </summary>
    public bool IsUpload => Upload is not null;

    /// <summary>
    /// Gets a value indicating whether the source is a document being composed.
    /// </summary>
    public bool IsComposed => Working?.IsComposed == true;

    /// <summary>
    /// Creates a source for an upload.
    /// </summary>
    /// <param name="upload">The upload.</param>
    /// <returns>The source.</returns>
    public static PdfSource ForUpload(AIDocument upload)
    {
        ArgumentNullException.ThrowIfNull(upload);

        return new PdfSource(upload.FileName, upload, null);
    }

    /// <summary>
    /// Creates a source for a working document.
    /// </summary>
    /// <param name="working">The working document.</param>
    /// <returns>The source.</returns>
    public static PdfSource ForWorking(PdfWorkingDocument working)
    {
        ArgumentNullException.ThrowIfNull(working);

        return new PdfSource(working.Name, null, working);
    }

    /// <summary>
    /// Describes the source for a message.
    /// </summary>
    /// <returns>For example <c>uploaded "report.pdf"</c> or <c>working PDF "report"</c>.</returns>
    public string Describe()
    {
        return IsUpload
            ? $"uploaded \"{Name}\""
            : $"working PDF \"{Name}\"";
    }
}
