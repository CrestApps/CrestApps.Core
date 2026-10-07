namespace CrestApps.Core.AI.Documents.Presentations.Models;

/// <summary>
/// One outline of a freeform shape.
/// </summary>
public sealed class PresentationPath
{
    /// <summary>
    /// Gets or sets a value indicating whether the area inside the outline is filled.
    /// </summary>
    public bool Filled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the outline itself is drawn.
    /// </summary>
    public bool Stroked { get; set; } = true;

    /// <summary>
    /// Gets or sets the drawing commands, in order.
    /// </summary>
    public IList<PresentationPathCommand> Commands { get; set; } = [];
}
