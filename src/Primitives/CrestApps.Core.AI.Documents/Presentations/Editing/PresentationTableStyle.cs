namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// How a slide table should look. Every property is optional.
/// </summary>
/// <remarks>
/// Tables are written with explicit cell fills and borders rather than a built-in table style. A built-in
/// style is drawn by PowerPoint from a definition that is not in the file, so the preview could only guess at
/// it; explicit formatting is drawn the same way by both.
/// </remarks>
public sealed class PresentationTableStyle
{
    /// <summary>
    /// Gets or sets the header row background.
    /// </summary>
    public string HeaderFill { get; set; }

    /// <summary>
    /// Gets or sets the header row text colour.
    /// </summary>
    public string HeaderTextColor { get; set; }

    /// <summary>
    /// Gets or sets whether header text is bold.
    /// </summary>
    public bool? HeaderBold { get; set; }

    /// <summary>
    /// Gets or sets the background of body rows.
    /// </summary>
    public string BodyFill { get; set; }

    /// <summary>
    /// Gets or sets the background of every other body row, when rows are banded.
    /// </summary>
    public string BandFill { get; set; }

    /// <summary>
    /// Gets or sets the body text colour.
    /// </summary>
    public string TextColor { get; set; }

    /// <summary>
    /// Gets or sets the border colour, or <c>none</c> for no borders.
    /// </summary>
    public string BorderColor { get; set; }

    /// <summary>
    /// Gets or sets the border thickness in points.
    /// </summary>
    public double? BorderWidth { get; set; }

    /// <summary>
    /// Gets or sets the typeface of the table text.
    /// </summary>
    public string Font { get; set; }

    /// <summary>
    /// Gets or sets the size of the table text in points.
    /// </summary>
    public double? FontSize { get; set; }

    /// <summary>
    /// Gets or sets whether the first column is bold.
    /// </summary>
    public bool? FirstColumnBold { get; set; }

    /// <summary>
    /// Gets or sets whether the last row is styled as a total: bold, with a rule above it.
    /// </summary>
    public bool? TotalRow { get; set; }

    /// <summary>
    /// Gets or sets whether alternate body rows are shaded with <see cref="BandFill"/>.
    /// </summary>
    public bool? BandedRows { get; set; }

    /// <summary>
    /// Gets a value indicating whether the style sets anything at all.
    /// </summary>
    public bool IsEmpty =>
        HeaderFill is null && HeaderTextColor is null && HeaderBold is null && BodyFill is null &&
        BandFill is null && TextColor is null && BorderColor is null && BorderWidth is null && Font is null &&
        FontSize is null && FirstColumnBold is null && TotalRow is null && BandedRows is null;

    /// <summary>
    /// Layers another style on top of this one: every property the other style sets wins.
    /// </summary>
    /// <param name="overrides">The style laid on top, or <see langword="null"/> for none.</param>
    /// <returns>A new style holding the combined properties.</returns>
    public PresentationTableStyle Merge(PresentationTableStyle overrides)
    {
        if (overrides is null)
        {
            return Clone();
        }

        return new PresentationTableStyle
        {
            HeaderFill = overrides.HeaderFill ?? HeaderFill,
            HeaderTextColor = overrides.HeaderTextColor ?? HeaderTextColor,
            HeaderBold = overrides.HeaderBold ?? HeaderBold,
            BodyFill = overrides.BodyFill ?? BodyFill,
            BandFill = overrides.BandFill ?? BandFill,
            TextColor = overrides.TextColor ?? TextColor,
            BorderColor = overrides.BorderColor ?? BorderColor,
            BorderWidth = overrides.BorderWidth ?? BorderWidth,
            Font = overrides.Font ?? Font,
            FontSize = overrides.FontSize ?? FontSize,
            FirstColumnBold = overrides.FirstColumnBold ?? FirstColumnBold,
            TotalRow = overrides.TotalRow ?? TotalRow,
            BandedRows = overrides.BandedRows ?? BandedRows,
        };
    }

    /// <summary>
    /// Copies the style.
    /// </summary>
    /// <returns>The copy.</returns>
    public PresentationTableStyle Clone()
    {
        return (PresentationTableStyle)MemberwiseClone();
    }

    /// <summary>
    /// Layers two styles, either of which may be missing.
    /// </summary>
    /// <param name="baseStyle">The style underneath.</param>
    /// <param name="overrides">The style laid on top.</param>
    /// <returns>The combined style, or <see langword="null"/> when neither is set.</returns>
    public static PresentationTableStyle Combine(PresentationTableStyle baseStyle, PresentationTableStyle overrides)
    {
        if (baseStyle is null)
        {
            return overrides?.Clone();
        }

        return baseStyle.Merge(overrides);
    }
}
