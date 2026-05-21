using CrestApps.Core.Models;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents a user memory entry stored for personalized AI context, capturing a named piece of information about the user.
/// </summary>
public sealed class AIMemoryEntry : CatalogItem
{
    /// <summary>
    /// Gets or sets the identifier of the user this memory entry belongs to.
    /// </summary>
    public string UserId { get; set; }

    /// <summary>
    /// Gets or sets the short name or label for this memory entry.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets a brief description of what this memory entry represents.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the full text content of this memory entry.
    /// </summary>
    public string Content { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when this memory entry was created.
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when this memory entry was last updated.
    /// </summary>
    public DateTime UpdatedUtc { get; set; }
}
