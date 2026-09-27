namespace CrestApps.Core.AI.Documents.Presentations;

/// <summary>
/// A slide table or chart tied to a query over the conversation's tabular data, so it can be refreshed when
/// the data changes.
/// </summary>
public sealed class PresentationDataLink
{
    /// <summary>
    /// Gets or sets the identifier of the slide the element is on.
    /// </summary>
    public uint SlideId { get; set; }

    /// <summary>
    /// Gets or sets the element's identifier on that slide.
    /// </summary>
    public uint ElementId { get; set; }

    /// <summary>
    /// Gets or sets what the element is: <c>table</c> or <c>chart</c>.
    /// </summary>
    public string Kind { get; set; }

    /// <summary>
    /// Gets or sets the read-only query the element's data comes from.
    /// </summary>
    public string Sql { get; set; }

    /// <summary>
    /// Gets or sets the column a chart takes its categories from; the first column when not set.
    /// </summary>
    public string CategoryColumn { get; set; }

    /// <summary>
    /// Gets or sets the columns a chart plots; every numeric column after the category when not set.
    /// </summary>
    public IList<string> ValueColumns { get; set; } = [];

    /// <summary>
    /// Gets or sets the most rows a table or chart takes from the query.
    /// </summary>
    public int MaxRows { get; set; } = 25;

    /// <summary>
    /// Gets or sets when the element was last filled from the query.
    /// </summary>
    public DateTime RefreshedUtc { get; set; }
}
