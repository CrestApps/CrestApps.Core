namespace CrestApps.Core.AI.Documents.Models;

/// <summary>
/// Represents the interaction Document Settings.
/// </summary>
public sealed class InteractionDocumentSettings
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

    /// <summary>
    /// Gets or sets how much extracted text a single uploaded document may hold and still be indexed.
    /// </summary>
    /// <remarks>
    /// A document past this is refused at upload rather than stored and left unsearchable: accepting a file
    /// is a promise to ingest it. Raising it costs embedding calls on every upload that now fits, which is
    /// why it is a setting rather than a constant, and why a profile that ingests large documents can raise
    /// it without changing what every other profile pays.
    /// </remarks>
    public int MaxIndexableCharacters { get; set; } = 50000;
}
