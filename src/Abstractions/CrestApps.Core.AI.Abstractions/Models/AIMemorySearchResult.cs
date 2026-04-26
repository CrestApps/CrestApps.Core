namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents a single result returned from a memory vector search, including the matched content and its relevance score.
/// </summary>
public sealed class AIMemorySearchResult
{
    /// <summary>
    /// Gets or sets the unique identifier of the matched memory entry.
    /// </summary>
    public string MemoryId { get; set; }

    /// <summary>
    /// Gets or sets the name or label of the matched memory entry.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the description of the matched memory entry.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the text content of the matched memory entry.
    /// </summary>
    public string Content { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the matched memory entry was last updated.
    /// </summary>
    public DateTime? UpdatedUtc { get; set; }

    /// <summary>
    /// Gets or sets the similarity score of this result (0.0–1.0), where higher values indicate closer relevance.
    /// </summary>
    public float Score { get; set; }
}
