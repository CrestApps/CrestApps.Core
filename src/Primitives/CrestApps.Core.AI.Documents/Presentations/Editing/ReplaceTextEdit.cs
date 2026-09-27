namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// Finds and replaces text across slides, keeping the formatting of the text it replaces.
/// </summary>
public sealed class ReplaceTextEdit : PresentationEdit
{
    /// <summary>
    /// Gets or sets the numbers of the slides to search. Every slide is searched when the list is empty.
    /// </summary>
    public IList<int> Slides { get; set; } = [];

    /// <summary>
    /// Gets or sets the text to find.
    /// </summary>
    public string Find { get; set; }

    /// <summary>
    /// Gets or sets the text to put in its place.
    /// </summary>
    public string Replace { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the case of the text must match.
    /// </summary>
    public bool MatchCase { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether only whole words match.
    /// </summary>
    public bool WholeWord { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether speaker notes are searched too.
    /// </summary>
    public bool IncludeNotes { get; set; }
}
