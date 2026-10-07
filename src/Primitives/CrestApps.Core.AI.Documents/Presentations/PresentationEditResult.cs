namespace CrestApps.Core.AI.Documents.Presentations;

/// <summary>
/// The outcome of applying a batch of edits.
/// </summary>
public sealed class PresentationEditResult
{
    /// <summary>
    /// Gets or sets the edited package.
    /// </summary>
    public byte[] Package { get; set; }

    /// <summary>
    /// Gets or sets one sentence per change made, in order, for the tool to report.
    /// </summary>
    public IList<string> Changes { get; set; } = [];

    /// <summary>
    /// Gets or sets things the caller should know that did not stop the edit, such as text that will not fit
    /// its box.
    /// </summary>
    public IList<string> Warnings { get; set; } = [];

    /// <summary>
    /// Gets or sets the elements the batch created.
    /// </summary>
    public IList<PresentationCreatedElement> CreatedElements { get; set; } = [];

    /// <summary>
    /// Gets or sets the identifiers of the slides the batch created.
    /// </summary>
    public IList<uint> CreatedSlideIds { get; set; } = [];

    /// <summary>
    /// Gets or sets the identifiers of the slides the batch changed or created, so a follow-up preview can
    /// show exactly those.
    /// </summary>
    public ISet<uint> ChangedSlideIds { get; set; } = new HashSet<uint>();

    /// <summary>
    /// Gets or sets the numbers of the slides the batch changed or created, after every edit in it.
    /// </summary>
    public IList<int> ChangedSlideNumbers { get; set; } = [];

    /// <summary>
    /// Gets or sets how many slides the deck has after the batch.
    /// </summary>
    public int SlideCount { get; set; }
}
