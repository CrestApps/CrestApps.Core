namespace CrestApps.Core.AI.Documents.Models;

/// <summary>
/// Represents the chat Documents Options.
/// </summary>
public sealed class ChatDocumentsOptions
{
    // 20 MB default limit for vision input to prevent excessive memory usage. This can be adjusted based on expected use cases and system capabilities.
    private const long DefaultMaxVisionInputBytesPerRequest = 20 * 1024 * 1024;

    // 10 MB default per-file limit to prevent a single image from consuming excessive memory.
    private const long DefaultMaxVisionImageBytesPerFile = 10 * 1024 * 1024;

    private readonly HashSet<string> _allowedFileExtensions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _embeddableFileExtensions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _tabularFileExtensions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the allowed File Extensions.
    /// </summary>
    public IReadOnlySet<string> AllowedFileExtensions => _allowedFileExtensions;

    /// <summary>
    /// Gets the embeddable File Extensions.
    /// </summary>
    public IReadOnlySet<string> EmbeddableFileExtensions => _embeddableFileExtensions;

    /// <summary>
    /// Gets the tabular file extensions.
    /// </summary>
    public IReadOnlySet<string> TabularFileExtensions => _tabularFileExtensions;

    /// <summary>
    /// Gets or sets the maximum total number of image bytes loaded into a single multimodal chat request.
    /// Set to <c>0</c> or a negative value to disable the limit.
    /// </summary>
    public long MaxVisionInputBytesPerRequest { get; set; } = DefaultMaxVisionInputBytesPerRequest;

    /// <summary>
    /// Gets or sets the maximum number of bytes for a single vision image file.
    /// Images exceeding this limit are skipped during orchestration.
    /// Set to <c>0</c> or a negative value to disable the per-file limit.
    /// </summary>
    public long MaxVisionImageBytesPerFile { get; set; } = DefaultMaxVisionImageBytesPerFile;

    /// <summary>
    /// Gets or sets a value indicating whether images should be analyzed at upload time
    /// using a vision model to extract caption, OCR text, and detected entities.
    /// When enabled, analysis results are stored as document chunks so the model can
    /// access image information via text-based tools instead of raw byte injection.
    /// </summary>
    public bool AnalyzeImagesAtUpload { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of <c>inspect_image</c> tool invocations
    /// allowed per chat request. This limits the cost of on-demand raw image inspection.
    /// </summary>
    public int MaxInspectImageCallsPerRequest { get; set; } = 2;

    internal void Add(string extension, bool embeddable = true, bool isTabular = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);

        var normalized = extension.StartsWith('.') ? extension : '.' + extension;

        _allowedFileExtensions.Add(normalized);

        if (embeddable && !isTabular)
        {
            _embeddableFileExtensions.Add(normalized);
        }
        else
        {
            _embeddableFileExtensions.Remove(normalized);
        }

        if (isTabular)
        {
            _tabularFileExtensions.Add(normalized);
        }
        else
        {
            _tabularFileExtensions.Remove(normalized);
        }
    }

    internal void Add(ExtractorExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);

        Add(extension.Extension, extension.Embeddable, extension.IsTabular);
    }
}
