namespace CrestApps.Core.AI.Documents.Presentations;

/// <summary>
/// Options that keep a slide preview to something the chat can show and the reader can take in.
/// </summary>
public sealed class PresentationPreviewOptions
{
    /// <summary>
    /// Gets or sets the most slides drawn by one preview call. The preview says which slides it left out.
    /// Default is 8.
    /// </summary>
    public int MaxSlides { get; set; } = 8;

    /// <summary>
    /// Gets or sets the width a slide picture is drawn at, in pixels. Default is 960.
    /// </summary>
    public int Width { get; set; } = 960;

    /// <summary>
    /// Gets or sets the widest a caller may ask a slide picture to be, in pixels. Default is 1920.
    /// </summary>
    public int MaxWidth { get; set; } = 1920;

    /// <summary>
    /// Gets or sets the most picture bytes one slide picture may embed; pictures past it are drawn as
    /// labelled placeholders. Default is 6 MB.
    /// </summary>
    public long MaxImageBytesPerSlide { get; set; } = 6 * 1024 * 1024;
}
