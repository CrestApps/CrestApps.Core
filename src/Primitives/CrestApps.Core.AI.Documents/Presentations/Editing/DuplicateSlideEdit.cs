namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// Copies a slide, with its elements, pictures, charts and notes.
/// </summary>
public sealed class DuplicateSlideEdit : PresentationEdit
{
    /// <summary>
    /// Gets or sets the number of the slide to copy.
    /// </summary>
    public int Slide { get; set; }

    /// <summary>
    /// Gets or sets where the copy goes, counting from 1. The copy follows the original when this is not
    /// set.
    /// </summary>
    public int? Position { get; set; }
}
