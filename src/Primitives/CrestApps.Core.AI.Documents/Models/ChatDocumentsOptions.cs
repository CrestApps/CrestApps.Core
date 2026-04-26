namespace CrestApps.Core.AI.Documents.Models;

/// <summary>
/// Represents the chat Documents Options.
/// </summary>
public sealed class ChatDocumentsOptions
{
    private readonly HashSet<string> _allowedFileExtensions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _embeddableFileExtensions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the allowed File Extensions.
    /// </summary>
    public IReadOnlySet<string> AllowedFileExtensions => _allowedFileExtensions;

    /// <summary>
    /// Gets the embeddable File Extensions.
    /// </summary>
    public IReadOnlySet<string> EmbeddableFileExtensions => _embeddableFileExtensions;

    internal void Add(string extension, bool embeddable = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);

        var normalized = extension.StartsWith('.') ? extension : '.' + extension;

        _allowedFileExtensions.Add(normalized);

        if (embeddable)
        {
            _embeddableFileExtensions.Add(normalized);
        }
        else
        {
            _embeddableFileExtensions.Remove(normalized);
        }
    }

    internal void Add(ExtractorExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);

        Add(extension.Extension, extension.Embeddable);
    }
}
