namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents the tool Entry.
/// </summary>
public sealed class ToolEntry
{
    /// <summary>
    /// Gets or sets the item ID.
    /// </summary>
    public string ItemId { get; set; }

    /// <summary>
    /// Gets or sets the display Text.
    /// </summary>
    public string DisplayText { get; set; }

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether is Selected.
    /// </summary>
    public bool IsSelected { get; set; }
}
