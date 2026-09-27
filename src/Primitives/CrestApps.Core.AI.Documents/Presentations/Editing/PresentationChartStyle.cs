namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// How a slide chart should look. Every property is optional.
/// </summary>
public sealed class PresentationChartStyle
{
    /// <summary>
    /// Gets or sets the series colours, in order. A pie or doughnut uses them for its slices.
    /// </summary>
    public IList<string> Colors { get; set; }

    /// <summary>
    /// Gets or sets where the legend sits: <c>bottom</c>, <c>top</c>, <c>left</c>, <c>right</c>, or
    /// <c>none</c> to hide it.
    /// </summary>
    public string Legend { get; set; }

    /// <summary>
    /// Gets or sets whether each point is labelled with its value.
    /// </summary>
    public bool? DataLabels { get; set; }

    /// <summary>
    /// Gets or sets whether horizontal gridlines are drawn behind the data.
    /// </summary>
    public bool? Gridlines { get; set; }

    /// <summary>
    /// Gets or sets the typeface of the chart text.
    /// </summary>
    public string Font { get; set; }

    /// <summary>
    /// Gets or sets the size of the chart text in points.
    /// </summary>
    public double? FontSize { get; set; }

    /// <summary>
    /// Gets or sets the colour of the chart text.
    /// </summary>
    public string TextColor { get; set; }

    /// <summary>
    /// Gets or sets the number format applied to values, for example <c>#,##0</c>, <c>$#,##0</c> or
    /// <c>0%</c>.
    /// </summary>
    public string NumberFormat { get; set; }

    /// <summary>
    /// Gets a value indicating whether the style sets anything at all.
    /// </summary>
    public bool IsEmpty =>
        Colors is null && Legend is null && DataLabels is null && Gridlines is null && Font is null &&
        FontSize is null && TextColor is null && NumberFormat is null;

    /// <summary>
    /// Layers another style on top of this one: every property the other style sets wins.
    /// </summary>
    /// <param name="overrides">The style laid on top, or <see langword="null"/> for none.</param>
    /// <returns>A new style holding the combined properties.</returns>
    public PresentationChartStyle Merge(PresentationChartStyle overrides)
    {
        if (overrides is null)
        {
            return Clone();
        }

        return new PresentationChartStyle
        {
            Colors = overrides.Colors is null ? Colors?.ToList() : [.. overrides.Colors],
            Legend = overrides.Legend ?? Legend,
            DataLabels = overrides.DataLabels ?? DataLabels,
            Gridlines = overrides.Gridlines ?? Gridlines,
            Font = overrides.Font ?? Font,
            FontSize = overrides.FontSize ?? FontSize,
            TextColor = overrides.TextColor ?? TextColor,
            NumberFormat = overrides.NumberFormat ?? NumberFormat,
        };
    }

    /// <summary>
    /// Copies the style.
    /// </summary>
    /// <returns>The copy.</returns>
    public PresentationChartStyle Clone()
    {
        var clone = (PresentationChartStyle)MemberwiseClone();
        clone.Colors = Colors is null ? null : [.. Colors];

        return clone;
    }

    /// <summary>
    /// Layers two styles, either of which may be missing.
    /// </summary>
    /// <param name="baseStyle">The style underneath.</param>
    /// <param name="overrides">The style laid on top.</param>
    /// <returns>The combined style, or <see langword="null"/> when neither is set.</returns>
    public static PresentationChartStyle Combine(PresentationChartStyle baseStyle, PresentationChartStyle overrides)
    {
        if (baseStyle is null)
        {
            return overrides?.Clone();
        }

        return baseStyle.Merge(overrides);
    }
}
