namespace CrestApps.Core.AI.Models;

/// <summary>
/// The detail a figure carries beyond the fields every knowledge object has.
/// </summary>
public sealed class FigureDetails
{
    /// <summary>
    /// Gets or sets the caption printed with the figure.
    /// </summary>
    public string Caption { get; set; }

    /// <summary>
    /// Gets or sets how the caption was arrived at, so a reader can tell a printed caption from an inference.
    /// </summary>
    public string CaptionSource { get; set; }

    /// <summary>
    /// Gets or sets the surrounding body text that gives the figure its meaning.
    /// </summary>
    public string Context { get; set; }

    /// <summary>
    /// Gets or sets what the figure was judged to be worth: skipped, kept for its caption, or transcribed.
    /// </summary>
    public string Tier { get; set; }

    /// <summary>
    /// Gets or sets the transcription of the figure.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the deployment that produced the transcription.
    /// </summary>
    public string DescriptionModel { get; set; }

    /// <summary>
    /// Gets or sets the prompt version the transcription was produced with, so a prompt change is not hidden
    /// behind a stale description.
    /// </summary>
    public string DescriptionPromptVersion { get; set; }

    /// <summary>
    /// Gets or sets why transcription failed, when it did.
    /// </summary>
    public string Error { get; set; }

    /// <summary>
    /// Gets or sets the figure width, in image samples.
    /// </summary>
    public int? PixelWidth { get; set; }

    /// <summary>
    /// Gets or sets the figure height, in image samples.
    /// </summary>
    public int? PixelHeight { get; set; }
}

/// <summary>
/// How far the values read off a chart can be trusted.
/// </summary>
/// <remarks>
/// This distinction is the difference between a useful answer and a confidently wrong one. A number read off
/// a raster image by eye looks exactly like a number lifted from the file's own geometry, and only one of
/// them is true.
/// </remarks>
public static class ChartValueConfidence
{
    /// <summary>
    /// The values came from the document itself: vector geometry, or data labels printed on the chart.
    /// </summary>
    public const string Exact = "Exact";

    /// <summary>
    /// Only the axes and the legend could be read. The series values were not printed.
    /// </summary>
    public const string AxesOnly = "AxesOnly";

    /// <summary>
    /// Only the shape and the trend are known. No value here is machine-readable, and none is stored.
    /// </summary>
    public const string Descriptive = "Descriptive";
}

/// <summary>
/// The kinds of chart that can be told apart from the drawing itself.
/// </summary>
/// <remarks>
/// A chart is only typed when the way it was drawn says what it is. Values printed on a plot say what the
/// numbers are and nothing about how they were carried, so a chart read that way carries no type at all. A
/// missing type costs a reader one detail; an invented one tells them something untrue about the document.
/// </remarks>
public static class ChartTypes
{
    /// <summary>
    /// The values were followed along the sloped polylines the chart draws, which is what a line chart is
    /// made of. A bar or column chart draws nothing sloped to follow, so it never reads as one.
    /// </summary>
    public const string Line = "Line";
}

/// <summary>
/// One named series of a chart.
/// </summary>
public sealed class ChartSeries
{
    /// <summary>
    /// Gets or sets the series name, as printed in the legend.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the points, each an x and a y. Populated only when the values came from the document.
    /// </summary>
    public IList<double[]> Points { get; set; } = [];
}

/// <summary>
/// The detail a chart carries beyond what a figure carries.
/// </summary>
public sealed class ChartDetails
{
    /// <summary>
    /// Gets or sets the kind of chart, such as a bar, line or scatter chart.
    /// </summary>
    public string ChartType { get; set; }

    /// <summary>
    /// Gets or sets how far the series values can be trusted. See <see cref="ChartValueConfidence"/>.
    /// </summary>
    public string ValueConfidence { get; set; } = ChartValueConfidence.Descriptive;

    /// <summary>
    /// Gets or sets the series. A chart whose confidence is descriptive carries none: an estimate is never
    /// stored as a value.
    /// </summary>
    public IList<ChartSeries> Series { get; set; } = [];

    /// <summary>
    /// Gets or sets the horizontal axis title, including its units.
    /// </summary>
    public string AxisX { get; set; }

    /// <summary>
    /// Gets or sets the vertical axis title, including its units.
    /// </summary>
    public string AxisY { get; set; }
}

/// <summary>
/// The detail a table carries beyond the fields every knowledge object has.
/// </summary>
public sealed class TableDetails
{
    /// <summary>
    /// Gets or sets the caption printed with the table.
    /// </summary>
    public string Caption { get; set; }

    /// <summary>
    /// Gets or sets the column headers.
    /// </summary>
    public IList<string> Columns { get; set; } = [];

    /// <summary>
    /// Gets or sets the rows, each holding one value per column.
    /// </summary>
    public IList<string[]> Rows { get; set; } = [];
}

/// <summary>
/// The detail an article carries beyond the fields every knowledge object has.
/// </summary>
public sealed class ArticleDetails
{
    /// <summary>
    /// Gets or sets the authors credited with the article.
    /// </summary>
    public IList<string> Authors { get; set; } = [];

    /// <summary>
    /// Gets or sets the running-head label that says what kind of article this is.
    /// </summary>
    public string SectionLabel { get; set; }

    /// <summary>
    /// Gets or sets the abstract, or the opening paragraph when none is printed.
    /// </summary>
    public string Abstract { get; set; }
}
