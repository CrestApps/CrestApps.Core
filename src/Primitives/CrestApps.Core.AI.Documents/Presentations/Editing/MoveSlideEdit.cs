namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// Moves a slide to another position.
/// </summary>
public sealed class MoveSlideEdit : PresentationEdit
{
    /// <summary>
    /// Gets or sets the number of the slide to move.
    /// </summary>
    public int Slide { get; set; }

    /// <summary>
    /// Gets or sets the position the slide ends up at, counting from 1.
    /// </summary>
    public int Position { get; set; }
}
