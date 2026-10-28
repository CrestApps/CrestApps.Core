namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// A chart ready to draw: its kind, data and look.
/// </summary>
internal sealed class PdfChartModel
{
    /// <summary>
    /// Gets or sets the kind of chart.
    /// </summary>
    public PdfChartKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the title written over the chart, or <see langword="null"/>.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// Gets or sets the category labels, one per value of each series.
    /// </summary>
    public IReadOnlyList<string> Labels { get; set; } = [];

    /// <summary>
    /// Gets or sets the series.
    /// </summary>
    public IReadOnlyList<PdfChartSeriesModel> Series { get; set; } = [];

    /// <summary>
    /// Gets or sets whether a legend names the series, or a pie's slices.
    /// </summary>
    public bool ShowLegend { get; set; }

    /// <summary>
    /// Gets or sets whether the values are written on the chart: each bar's or point's value, or a slice's share.
    /// </summary>
    public bool ShowDataLabels { get; set; }

    /// <summary>
    /// Gets or sets the .NET number format of the value axis and the value labels, such as <c>#,##0</c>.
    /// </summary>
    public string NumberFormat { get; set; } = "#,##0";

    /// <summary>
    /// Gets or sets the title of the category axis, or <see langword="null"/>.
    /// </summary>
    public string CategoryAxisTitle { get; set; }

    /// <summary>
    /// Gets or sets the title of the value axis, or <see langword="null"/>.
    /// </summary>
    public string ValueAxisTitle { get; set; }

    /// <summary>
    /// Gets or sets the font family.
    /// </summary>
    public string FontFamily { get; set; } = PdfFontFamilies.Default;

    /// <summary>
    /// Gets or sets the size of the labels, in points.
    /// </summary>
    public double FontSize { get; set; } = 8;

    /// <summary>
    /// Gets or sets the size of the title, in points.
    /// </summary>
    public double TitleSize { get; set; } = 10;

    /// <summary>
    /// Gets or sets the colour of the labels.
    /// </summary>
    public PdfColor TextColor { get; set; } = new(0x33, 0x33, 0x33);

    /// <summary>
    /// Gets or sets the colour of the title.
    /// </summary>
    public PdfColor TitleColor { get; set; } = new(0, 0, 0);

    /// <summary>
    /// Gets or sets the colour of the gridlines.
    /// </summary>
    public PdfColor GridColor { get; set; } = new(0xE0, 0xE0, 0xE0);

    /// <summary>
    /// Gets or sets the colour of the category axis line.
    /// </summary>
    public PdfColor AxisColor { get; set; } = new(0xA0, 0xA0, 0xA0);

    /// <summary>
    /// Gets or sets the colour of each pie slice, by its index.
    /// </summary>
    public Func<int, PdfColor> SliceColor { get; set; } = _ => new PdfColor(0x44, 0x72, 0xC4);
}
