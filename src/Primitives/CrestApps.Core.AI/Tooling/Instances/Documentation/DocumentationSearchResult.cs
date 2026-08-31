namespace CrestApps.Core.AI.Tooling.Instances.Documentation;

/// <summary>
/// Represents a single relevant document returned by an <see cref="IDocumentationSource"/>.
/// </summary>
public sealed class DocumentationSearchResult
{
    /// <summary>
    /// Gets or sets the logical name of the source that produced this result.
    /// </summary>
    public string SourceName { get; set; }

    /// <summary>
    /// Gets or sets the title of the matching document.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the canonical URL of the matching document.
    /// </summary>
    public string Url { get; set; }

    /// <summary>
    /// Gets or sets a short text excerpt that highlights the match.
    /// </summary>
    public string Snippet { get; set; }

    /// <summary>
    /// Gets or sets the relevance score. Higher values indicate a stronger match. Scores are only
    /// comparable within a single source.
    /// </summary>
    public double Score { get; set; }
}
