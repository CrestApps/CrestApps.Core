namespace CrestApps.Core.AI.Documents.Word;

/// <summary>
/// Bounds how much of a Word document the agent draws when it shows pages in the conversation.
/// </summary>
public sealed class WordPreviewOptions
{
    /// <summary>
    /// Gets or sets the number of pages a preview draws when no pages are asked for: the first pages of the
    /// document, up to this many. The preview says how many pages the document has, so the reader can ask for
    /// others by number. Defaults to 4.
    /// </summary>
    public int MaxPages { get; set; } = 4;

    /// <summary>
    /// Gets or sets the most pages one preview draws when pages are asked for by number, such as <c>1-10</c>.
    /// The preview says which of the requested pages it left out. A value below <see cref="MaxPages"/> is
    /// treated as <see cref="MaxPages"/>. Defaults to 12.
    /// </summary>
    public int MaxRenderPages { get; set; } = 12;

    /// <summary>
    /// Gets or sets the most bytes of embedded pictures one drawn page may carry. Pictures past it are drawn as
    /// labelled placeholders, so a page of photographs does not become a preview too large to load. Defaults to
    /// 3 MB.
    /// </summary>
    public int MaxImageBytesPerPage { get; set; } = 3 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the width, in pixels, a drawn page is sized to. Defaults to 816, a letter page at 96 dpi.
    /// </summary>
    public int PageWidthPixels { get; set; } = 816;

    /// <summary>
    /// Gets or sets the most pages the layout engine lays out for one document. A document longer than this is
    /// laid out up to the limit, and tools that count pages say the count is a lower bound. Defaults to 500.
    /// </summary>
    public int MaxLayoutPages { get; set; } = 500;
}
