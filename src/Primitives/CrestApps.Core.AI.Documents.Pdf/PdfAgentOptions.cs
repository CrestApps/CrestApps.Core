namespace CrestApps.Core.AI.Documents.Pdf;

/// <summary>
/// Configures the system PDF agent and the limits its tools work within.
/// </summary>
public sealed class PdfAgentOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the system PDF agent is offered to the model. The PDF reader
    /// and writer work either way; turning this off only removes the agent, and the description of it that
    /// every request carries. Defaults to <see langword="true"/>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the largest PDF, in bytes, the agent opens or keeps as a working copy. Defaults to 50 MB.
    /// </summary>
    public long MaxDocumentBytes { get; set; } = 50L * 1024 * 1024;

    /// <summary>
    /// Gets or sets the most working documents one conversation keeps. Defaults to 40.
    /// </summary>
    public int MaxWorkingDocuments { get; set; } = 40;

    /// <summary>
    /// Gets or sets the most characters a tool hands back to the model in one answer. Longer results are cut
    /// and say how to ask for the rest. Defaults to 24,000.
    /// </summary>
    public int MaxToolResponseCharacters { get; set; } = 24_000;

    /// <summary>
    /// Gets or sets the most pages one OCR or image-analysis call sends to a vision model. Defaults to 8.
    /// </summary>
    public int MaxVisionPagesPerCall { get; set; } = 8;

    /// <summary>
    /// Gets or sets the largest picture, in bytes, a composed document places or a vision model is sent.
    /// Defaults to 10 MB.
    /// </summary>
    public int MaxImageBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the most characters of document text a single model-assisted tool (summaries,
    /// structured extraction, classification) reads in one pass before it summarizes in parts.
    /// Defaults to 60,000.
    /// </summary>
    public int MaxModelInputCharacters { get; set; } = 60_000;
}
