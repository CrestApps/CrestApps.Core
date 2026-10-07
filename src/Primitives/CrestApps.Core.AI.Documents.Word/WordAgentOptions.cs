namespace CrestApps.Core.AI.Documents.Word;

/// <summary>
/// Configures the system Word agent and the limits its tools work within.
/// </summary>
public sealed class WordAgentOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the system Word agent is offered to the model. The Word writer
    /// behind generated files works either way; turning this off only removes the agent, and the description of
    /// it that every request carries. Defaults to <see langword="true"/>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the largest Word document, in bytes, the agent opens or keeps as a working copy. Defaults to
    /// 50 MB.
    /// </summary>
    public long MaxDocumentBytes { get; set; } = 50L * 1024 * 1024;

    /// <summary>
    /// Gets or sets the most bytes a Word document may expand to once unpacked: the parts of a <c>.docx</c> file
    /// are compressed, and the document is opened in memory. An upload or template that expands beyond it, holds
    /// more than 10,000 parts, or has a large part compressed far more tightly than a document ever is, is refused
    /// before it is opened, and an edit is not saved when its result expands beyond it. Defaults to 256 MB.
    /// </summary>
    public long MaxUncompressedDocumentBytes { get; set; } = 256L * 1024 * 1024;

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
    /// Gets or sets the largest picture, in bytes, a document places. Defaults to 10 MB.
    /// </summary>
    public int MaxImageBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the author name comments and tracked changes made by the agent are signed with. Defaults to
    /// <c>AI Assistant</c>.
    /// </summary>
    public string Author { get; set; } = "AI Assistant";
}
