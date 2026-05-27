namespace CrestApps.Core.AI.Documents.Models;

/// <summary>
/// Represents the interaction Document Options.
/// </summary>
public sealed class InteractionDocumentOptions
{
    /// <summary>
    /// Gets or sets the index profile name to use for document embedding and search.
    /// </summary>
    public string IndexProfileName { get; set; }

    /// <summary>
    /// Gets or sets the number of top matching document chunks to include in AI context.
    /// Default is 3.
    /// </summary>
    public int TopN { get; set; } = 3;

    /// <summary>
    /// Gets or sets how retrieved document matches are added to AI context.
    /// </summary>
    public DocumentRetrievalMode RetrievalMode { get; set; } = DocumentRetrievalMode.Chunk;

    /// <summary>
    /// Gets or sets whether users are allowed to upload document files in chat interactions.
    /// Default is <see langword="true"/>.
    /// </summary>
    public bool AllowDocumentUploads { get; set; } = true;

    /// <summary>
    /// Gets or sets whether users are allowed to upload image files in chat interactions.
    /// When enabled, image uploads are processed using the global vision deployment.
    /// </summary>
    public bool AllowImageUploads { get; set; }
}
