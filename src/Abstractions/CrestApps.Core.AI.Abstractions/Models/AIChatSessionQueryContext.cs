namespace CrestApps.Core.AI.Models;

/// <summary>
/// Specifies filter criteria for querying a user's AI chat sessions.
/// </summary>
public sealed class AIChatSessionQueryContext
{
    /// <summary>
    /// Gets or sets the AI profile identifier to filter sessions by.
    /// </summary>
    public string ProfileId { get; set; }

    /// <summary>
    /// Gets or sets a name substring to filter sessions by their title.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether sessions should be returned in sorted order.
    /// </summary>
    public bool Sorted { get; set; }
}
