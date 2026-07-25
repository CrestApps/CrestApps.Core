namespace CrestApps.Core.Mvc.Web.Areas.Tooling.ViewModels;

/// <summary>
/// Represents a selectable tool definition shown when configuring an AI profile.
/// </summary>
public sealed class AIToolDefinitionSelectionItem
{
    /// <summary>
    /// Gets or sets the instance identifier.
    /// </summary>
    public string ItemId { get; set; }

    /// <summary>
    /// Gets or sets the instance display text.
    /// </summary>
    public string DisplayText { get; set; }

    /// <summary>
    /// Gets or sets the instance description shown to the model.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the definition name that produced the instance.
    /// </summary>
    public string Source { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the instance is selected.
    /// </summary>
    public bool IsSelected { get; set; }
}
