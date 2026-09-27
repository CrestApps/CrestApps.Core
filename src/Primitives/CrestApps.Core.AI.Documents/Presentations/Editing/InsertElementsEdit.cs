namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// Places new elements on a slide.
/// </summary>
public sealed class InsertElementsEdit : PresentationEdit
{
    /// <summary>
    /// Gets or sets the number of the slide.
    /// </summary>
    public int Slide { get; set; }

    /// <summary>
    /// Gets or sets the elements to insert, back to front.
    /// </summary>
    public IList<PresentationElementSpec> Elements { get; set; } = [];
}
