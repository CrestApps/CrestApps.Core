namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents a document reference returned alongside an AI completion response.
/// </summary>
public sealed class AICompletionReference
{
    /// <summary>
    /// Gets or sets the source text excerpt or citation text for this reference.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Gets or sets the URL link to the referenced document or resource.
    /// </summary>
    public string Link { get; set; }

    /// <summary>
    /// Gets or sets the display title of the referenced document.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the one-based display index for this reference within the response.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Gets or sets the raw reference identifier from the source index.
    /// </summary>
    public string ReferenceId { get; set; }

    /// <summary>
    /// Gets or sets the type of the reference source
    /// (e.g., the source index profile type for data sources, or "Document" for uploaded documents).
    /// Used to determine how links should be generated.
    /// </summary>
    public string ReferenceType { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this reference points to a file that was generated
    /// during the response (such as an exported tabular file). Generated references are always
    /// surfaced to the user as a download, even when the model does not cite them inline.
    /// </summary>
    public bool IsGenerated { get; set; }
}
