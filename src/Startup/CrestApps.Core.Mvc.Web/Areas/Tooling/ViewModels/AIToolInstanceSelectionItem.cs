namespace CrestApps.Core.Mvc.Web.Areas.Tooling.ViewModels;

/// <summary>
/// Represents a selectable tool instance shown when configuring an AI profile or chat interaction.
/// </summary>
public sealed class AIToolInstanceSelectionItem
{
    /// <summary>
    /// Gets or sets the instance identifier.
    /// </summary>
    public string ItemId { get; set; }

    /// <summary>
    /// Gets or sets the unique instance name used as the stable reference.
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
