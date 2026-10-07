namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// Restyles the deck after another deck or template: its theme colours and fonts, and optionally its slide
/// masters and layouts.
/// </summary>
public sealed class ApplyTemplateEdit : PresentationEdit
{
    /// <summary>
    /// Gets or sets the package of the deck or template to take the design from.
    /// </summary>
    public byte[] TemplatePackage { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the template's masters and layouts replace the deck's, with
    /// every slide moved onto the template layout that matches its own. When not set only the theme colours
    /// and fonts are taken.
    /// </summary>
    public bool ReplaceMasters { get; set; } = true;
}
