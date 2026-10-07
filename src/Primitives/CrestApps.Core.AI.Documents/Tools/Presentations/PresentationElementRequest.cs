using CrestApps.Core.AI.Documents.Presentations.Editing;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// An element a tool call asked for, with the parts that still have to be fetched before the engine can place
/// it: an uploaded picture, a generated one, or rows from the conversation's tabular data.
/// </summary>
internal sealed class PresentationElementRequest
{
    /// <summary>
    /// Gets or sets the element as far as the arguments describe it.
    /// </summary>
    public PresentationElementSpec Spec { get; set; }

    /// <summary>
    /// Gets or sets the uploaded picture to place, by document identifier or file name.
    /// </summary>
    public string ImageDocument { get; set; }

    /// <summary>
    /// Gets or sets a description of a picture to generate with the image model.
    /// </summary>
    public string ImagePrompt { get; set; }

    /// <summary>
    /// Gets or sets a read-only query over the conversation's tabular data that fills a table or chart.
    /// </summary>
    public string TabularSql { get; set; }

    /// <summary>
    /// Gets or sets the query column a chart takes its categories from.
    /// </summary>
    public string CategoryColumn { get; set; }

    /// <summary>
    /// Gets or sets the query columns a chart plots.
    /// </summary>
    public IList<string> ValueColumns { get; set; } = [];

    /// <summary>
    /// Gets or sets the most query rows used.
    /// </summary>
    public int MaxRows { get; set; } = 25;

    /// <summary>
    /// Gets or sets a value indicating whether the element stays tied to its query so it can be refreshed.
    /// </summary>
    public bool Link { get; set; }

    /// <summary>
    /// Gets or sets the requests of a group's elements.
    /// </summary>
    public IList<PresentationElementRequest> Children { get; set; } = [];
}
