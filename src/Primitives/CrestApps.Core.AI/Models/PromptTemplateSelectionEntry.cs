namespace CrestApps.Core.AI.Models;

/// <summary>
/// Represents the prompt Template Selection Entry.
/// </summary>
public sealed class PromptTemplateSelectionEntry
{
    /// <summary>
    /// Gets or sets the template ID.
    /// </summary>
    public string TemplateId { get; set; }

    /// <summary>
    /// Gets or sets the parameters.
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; }
}
