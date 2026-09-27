namespace CrestApps.Core.AI.Documents.Pdf.Workspace;

/// <summary>
/// A request a PDF tool cannot carry out, with a message written for the model to act on.
/// </summary>
/// <remarks>
/// Thrown for problems with what was asked — a PDF that does not exist, a page past the end, a password that
/// does not open the file — never for faults in the code. The tool returns the message instead of failing,
/// so the model can correct the call and try again.
/// </remarks>
public sealed class PdfToolException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfToolException"/> class.
    /// </summary>
    public PdfToolException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfToolException"/> class.
    /// </summary>
    /// <param name="message">The message for the model.</param>
    public PdfToolException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfToolException"/> class.
    /// </summary>
    /// <param name="message">The message for the model.</param>
    /// <param name="innerException">The underlying exception.</param>
    public PdfToolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
