namespace CrestApps.Core.AI.Documents.Word.Rendering;

/// <summary>
/// How a document is laid out.
/// </summary>
internal sealed class WordLayoutOptions
{
    /// <summary>
    /// Gets or sets the most pages laid out.
    /// </summary>
    public int MaxPages { get; set; } = 500;

    /// <summary>
    /// Gets or sets a value indicating whether pictures are read, rather than drawn as placeholders. Layout for
    /// page numbers alone does not need them.
    /// </summary>
    public bool IncludePictures { get; set; } = true;

    /// <summary>
    /// Gets or sets the most bytes of pictures one page carries before the rest are drawn as placeholders.
    /// </summary>
    public int MaxPictureBytesPerPage { get; set; } = 3 * 1024 * 1024;

    /// <summary>
    /// Gets or sets a value indicating whether tracked changes are drawn as markup — insertions underlined and
    /// deletions struck through — rather than as the document reads with every change accepted.
    /// </summary>
    public bool ShowMarkup { get; set; }

    /// <summary>
    /// Creates the options a preview uses from the preview settings.
    /// </summary>
    /// <param name="preview">The preview settings.</param>
    /// <returns>The options.</returns>
    public static WordLayoutOptions From(WordPreviewOptions preview)
    {
        preview ??= new WordPreviewOptions();

        return new WordLayoutOptions
        {
            MaxPages = Math.Max(1, preview.MaxLayoutPages),
            MaxPictureBytesPerPage = Math.Max(0, preview.MaxImageBytesPerPage),
        };
    }
}
