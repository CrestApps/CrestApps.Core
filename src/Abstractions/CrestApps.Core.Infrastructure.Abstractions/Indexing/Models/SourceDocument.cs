namespace CrestApps.Core.Infrastructure.Indexing.Models;

/// <summary>
/// Represents a document read from a source index with extracted title, content, and all source fields.
/// </summary>
public sealed class SourceDocument
{
    /// <summary>
    /// Gets or sets the document title.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the document content text.
    /// </summary>
    public string Content { get; set; }

    /// <summary>
    /// Gets or sets all fields from the source document.
    /// Used for populating filter fields in the knowledge base index.
    /// </summary>
    public Dictionary<string, object> Fields { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="Content"/> is already one chunk.
    /// </summary>
    /// <remarks>
    /// A typed knowledge object was built to sit inside the chunk budget and already carries its own title.
    /// Re-chunking it would split a figure description away from the figure it describes, and prepending the
    /// title again would duplicate it.
    /// </remarks>
    public bool IsPreChunked { get; set; }
}
