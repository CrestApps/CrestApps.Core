namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// How a shape should be painted and outlined. Every property is optional.
/// </summary>
public sealed class PresentationShapeStyle
{
    /// <summary>
    /// Gets or sets the fill colour, or <c>none</c> for no fill.
    /// </summary>
    public string Fill { get; set; }

    /// <summary>
    /// Gets or sets the colours of a gradient fill, in order, which replaces a solid fill.
    /// </summary>
    public IList<string> GradientColors { get; set; }

    /// <summary>
    /// Gets or sets the direction of a gradient fill, in degrees clockwise from left-to-right.
    /// </summary>
    public double? GradientAngle { get; set; }

    /// <summary>
    /// Gets or sets how transparent the fill is, from 0 (opaque) to 100 (invisible).
    /// </summary>
    public double? Transparency { get; set; }

    /// <summary>
    /// Gets or sets the outline colour, or <c>none</c> for no outline.
    /// </summary>
    public string OutlineColor { get; set; }

    /// <summary>
    /// Gets or sets the outline thickness in points.
    /// </summary>
    public double? OutlineWidth { get; set; }

    /// <summary>
    /// Gets or sets the outline dash pattern: <c>solid</c>, <c>dash</c>, <c>dot</c>, <c>dash_dot</c>,
    /// <c>long_dash</c> or <c>long_dash_dot</c>.
    /// </summary>
    public string OutlineDash { get; set; }

    /// <summary>
    /// Gets or sets whether the shape casts a soft shadow.
    /// </summary>
    public bool? Shadow { get; set; }

    /// <summary>
    /// Gets or sets the corner rounding of a rounded rectangle, from 0 (square) to 50 (fully round), as a
    /// percentage of its shorter side.
    /// </summary>
    public double? CornerRadius { get; set; }

    /// <summary>
    /// Gets a value indicating whether the style sets anything at all.
    /// </summary>
    public bool IsEmpty =>
        Fill is null && GradientColors is null && GradientAngle is null && Transparency is null &&
        OutlineColor is null && OutlineWidth is null && OutlineDash is null && Shadow is null && CornerRadius is null;

    /// <summary>
    /// Layers another style on top of this one: every property the other style sets wins.
    /// </summary>
    /// <param name="overrides">The style laid on top, or <see langword="null"/> for none.</param>
    /// <returns>A new style holding the combined properties.</returns>
    public PresentationShapeStyle Merge(PresentationShapeStyle overrides)
    {
        if (overrides is null)
        {
            return Clone();
        }

        // A gradient and a solid fill are alternatives, so setting one clears the other rather than the
        // older of the two silently winning.
        var fill = overrides.Fill ?? (overrides.GradientColors is not null ? null : Fill);
        var gradient = overrides.GradientColors ?? (overrides.Fill is not null ? null : GradientColors);

        return new PresentationShapeStyle
        {
            Fill = fill,
            GradientColors = gradient,
            GradientAngle = overrides.GradientAngle ?? GradientAngle,
            Transparency = overrides.Transparency ?? Transparency,
            OutlineColor = overrides.OutlineColor ?? OutlineColor,
            OutlineWidth = overrides.OutlineWidth ?? OutlineWidth,
            OutlineDash = overrides.OutlineDash ?? OutlineDash,
            Shadow = overrides.Shadow ?? Shadow,
            CornerRadius = overrides.CornerRadius ?? CornerRadius,
        };
    }

    /// <summary>
    /// Copies the style.
    /// </summary>
    /// <returns>The copy.</returns>
    public PresentationShapeStyle Clone()
    {
        var clone = (PresentationShapeStyle)MemberwiseClone();
        clone.GradientColors = GradientColors is null ? null : [.. GradientColors];

        return clone;
    }

    /// <summary>
    /// Layers two styles, either of which may be missing.
    /// </summary>
    /// <param name="baseStyle">The style underneath.</param>
    /// <param name="overrides">The style laid on top.</param>
    /// <returns>The combined style, or <see langword="null"/> when neither is set.</returns>
    public static PresentationShapeStyle Combine(PresentationShapeStyle baseStyle, PresentationShapeStyle overrides)
    {
        if (baseStyle is null)
        {
            return overrides?.Clone();
        }

        return baseStyle.Merge(overrides);
    }
}
