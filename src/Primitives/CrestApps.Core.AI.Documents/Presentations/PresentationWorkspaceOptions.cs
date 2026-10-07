namespace CrestApps.Core.AI.Documents.Presentations;

/// <summary>
/// Options that bound the presentation workspace a conversation keeps its decks in.
/// </summary>
public sealed class PresentationWorkspaceOptions
{
    private readonly HashSet<string> _extensions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the most decks one conversation can hold. Default is 10.
    /// </summary>
    public int MaxDecks { get; set; } = 10;

    /// <summary>
    /// Gets or sets how many earlier versions of each deck are kept so a change can be undone. Default is 5.
    /// </summary>
    public int MaxRevisions { get; set; } = 5;

    /// <summary>
    /// Gets or sets the largest deck, in bytes, the workspace keeps earlier versions of; a larger deck is still
    /// edited, but without undo, so a conversation does not fill the disk with copies. Default is 30 MB.
    /// </summary>
    public long MaxRevisionPackageBytes { get; set; } = 30 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the largest picture, in bytes, the presentation tools will place on a slide. Default is
    /// 15 MB.
    /// </summary>
    public long MaxImageBytes { get; set; } = 15 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the most slides a deck can be given by the tools. Default is 200.
    /// </summary>
    public int MaxSlides { get; set; } = 200;

    /// <summary>
    /// Gets the upload extensions the workspace imports as decks, registered by the engine that reads them.
    /// </summary>
    public IReadOnlySet<string> Extensions => _extensions;

    /// <summary>
    /// Registers an upload extension the workspace imports as a deck.
    /// </summary>
    /// <param name="extension">The extension, with or without a leading dot.</param>
    public void AddExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return;
        }

        var trimmed = extension.Trim();
        _extensions.Add(trimmed.StartsWith('.') ? trimmed : "." + trimmed);
    }

    /// <summary>
    /// Determines whether a file is one the workspace imports as a deck.
    /// </summary>
    /// <param name="fileName">The file name or extension.</param>
    /// <returns><see langword="true"/> when the file is a presentation.</returns>
    public bool IsPresentationFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var extension = Path.GetExtension(fileName);

        if (string.IsNullOrEmpty(extension))
        {
            extension = fileName.StartsWith('.') ? fileName : "." + fileName;
        }

        return _extensions.Contains(extension);
    }
}
