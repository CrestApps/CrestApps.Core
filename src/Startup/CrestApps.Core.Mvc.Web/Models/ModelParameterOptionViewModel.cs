namespace CrestApps.Core.Mvc.Web.Models;

/// <summary>
/// Represents a selectable value of a choice parameter.
/// </summary>
public sealed class ModelParameterOptionViewModel
{
    /// <summary>
    /// Gets or sets the technical value posted by the editor.
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    /// Gets or sets the display text shown to operators.
    /// </summary>
    public string DisplayName { get; set; }
}
