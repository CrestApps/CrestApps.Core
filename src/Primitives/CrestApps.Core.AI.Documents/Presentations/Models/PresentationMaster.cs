namespace CrestApps.Core.AI.Documents.Presentations.Models;

/// <summary>
/// A slide master and the layouts that belong to it.
/// </summary>
public sealed class PresentationMaster
{
    /// <summary>
    /// Gets or sets the master's position among the deck's masters, counting from 1.
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    /// Gets or sets the master's name, which is usually the name of its theme.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the master's background.
    /// </summary>
    public PresentationFill Background { get; set; } = PresentationFill.Solid("FFFFFF");

    /// <summary>
    /// Gets or sets the typeface, size and colour of titles on the master.
    /// </summary>
    public PresentationTextRun TitleStyle { get; set; }

    /// <summary>
    /// Gets or sets the typeface, size and colour of top-level body text on the master.
    /// </summary>
    public PresentationTextRun BodyStyle { get; set; }

    /// <summary>
    /// Gets or sets the layouts that belong to the master.
    /// </summary>
    public IList<PresentationLayout> Layouts { get; set; } = [];

    /// <summary>
    /// Gets or sets the number of decorative elements the master draws on every slide, such as a logo.
    /// </summary>
    public int DecorationCount { get; set; }
}
