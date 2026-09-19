namespace CrestApps.Core.Blazor.Web.ViewModels;

/// <summary>
/// Represents a selectable AI tool instance shown when configuring an AI profile.
/// </summary>
public sealed class AIToolInstanceSelectionItem
{
    /// <summary>
    /// Gets or sets the instance identifier.
    /// </summary>
    public string ItemId { get; set; }

    /// <summary>
    /// Gets or sets the unique instance name. Used as the stable reference stored on the profile.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the instance description shown to the model.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the source name that produced the instance.
    /// </summary>
    public string Source { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the instance is selected.
    /// </summary>
    public bool IsSelected { get; set; }

}
