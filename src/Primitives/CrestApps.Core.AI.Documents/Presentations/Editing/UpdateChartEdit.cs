namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// Changes a slide chart: its data, kind, titles and look. Properties left unset are kept.
/// </summary>
public sealed class UpdateChartEdit : PresentationEdit
{
    /// <summary>
    /// Gets or sets the number of the slide.
    /// </summary>
    public int Slide { get; set; }

    /// <summary>
    /// Gets or sets the chart, by identifier or name. The only chart on the slide is used when this is not
    /// set.
    /// </summary>
    public string Element { get; set; }

    /// <summary>
    /// Gets or sets the changes. Categories and series, when given, replace the chart's data.
    /// </summary>
    public PresentationChartSpec Chart { get; set; } = new();
}
