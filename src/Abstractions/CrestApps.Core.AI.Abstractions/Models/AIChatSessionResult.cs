namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents a paginated result set of AI chat session summaries.
/// </summary>
public sealed class AIChatSessionResult
{
    /// <summary>
    /// Gets or sets the total number of sessions matching the query (before pagination).
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// Gets or sets the page of session summary entries for the current request.
    /// </summary>
    public IEnumerable<AIChatSessionEntry> Sessions { get; set; }
}
