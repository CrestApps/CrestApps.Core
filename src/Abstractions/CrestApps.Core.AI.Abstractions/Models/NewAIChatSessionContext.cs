namespace CrestApps.Core.AI.Models;

/// <summary>
/// Context carrying initialization data when a new AI chat session is being created.
/// </summary>
public sealed class NewAIChatSessionContext
{
    /// <summary>
    /// Gets or sets a value indicating whether search engine robots are allowed to index this session.
    /// </summary>
    public bool AllowRobots { get; set; }
}
