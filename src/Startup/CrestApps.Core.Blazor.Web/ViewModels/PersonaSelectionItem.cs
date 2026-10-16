namespace CrestApps.Core.Blazor.Web.ViewModels;

/// <summary>
/// One option in an AI persona picker.
/// </summary>
public sealed class PersonaSelectionItem
{
    /// <summary>
    /// Gets or sets the technical name of the persona template, stored as <c>AIPersonaMetadata.PersonaId</c>.
    /// </summary>
    public string PersonaId { get; set; }

    /// <summary>
    /// Gets or sets the text shown for the persona.
    /// </summary>
    public string DisplayText { get; set; }

    /// <summary>
    /// Gets or sets the category the persona is grouped under, or <see langword="null"/> for none.
    /// </summary>
    public string Category { get; set; }

    /// <summary>
    /// Gets or sets the voice the persona speaks with, or <see langword="null"/> when it has none.
    /// </summary>
    public string VoiceName { get; set; }

    /// <summary>
    /// Gets or sets whether the persona is linked but no longer exists. It stays selectable so saving
    /// the form does not silently drop the link.
    /// </summary>
    public bool IsMissing { get; set; }
}
