namespace CrestApps.Core.AI.Documents.Pdf;

/// <summary>
/// Bounds how much of a PDF the agent draws when it shows pages in the conversation.
/// </summary>
public sealed class PdfPreviewOptions
{
    /// <summary>
    /// Gets or sets the most pages drawn by one preview. The preview says which pages it left out, so the
    /// reader can ask for them by number. Defaults to 4.
    /// </summary>
    public int MaxPages { get; set; } = 4;

    /// <summary>
    /// Gets or sets the most bytes of embedded pictures one drawn page may carry. Pictures past it are drawn
    /// as labelled placeholders, so a page of photographs does not become a preview too large to load.
    /// Defaults to 3 MB.
    /// </summary>
    public int MaxImageBytesPerPage { get; set; } = 3 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the most path segments drawn for one page. A page of dense vector art is simplified past
    /// it and says so. Defaults to 60,000.
    /// </summary>
    public int MaxPathSegmentsPerPage { get; set; } = 60_000;

    /// <summary>
    /// Gets or sets the width, in pixels, a drawn page is sized to. Defaults to 816, a letter page at 96 dpi.
    /// </summary>
    public int PageWidthPixels { get; set; } = 816;
}
