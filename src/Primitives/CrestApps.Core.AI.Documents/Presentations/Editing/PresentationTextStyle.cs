namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// How text should look. Every property is optional: an unset property leaves what the text already has, or
/// what it inherits, in place.
/// </summary>
public sealed class PresentationTextStyle
{
    /// <summary>
    /// Gets or sets the typeface.
    /// </summary>
    public string Font { get; set; }

    /// <summary>
    /// Gets or sets the size in points.
    /// </summary>
    public double? Size { get; set; }

    /// <summary>
    /// Gets or sets whether the text is bold.
    /// </summary>
    public bool? Bold { get; set; }

    /// <summary>
    /// Gets or sets whether the text is italic.
    /// </summary>
    public bool? Italic { get; set; }

    /// <summary>
    /// Gets or sets whether the text is underlined.
    /// </summary>
    public bool? Underline { get; set; }

    /// <summary>
    /// Gets or sets whether the text is struck through.
    /// </summary>
    public bool? Strikethrough { get; set; }

    /// <summary>
    /// Gets or sets the text colour, in any form <see cref="PresentationColor.TryParse"/> reads.
    /// </summary>
    public string Color { get; set; }

    /// <summary>
    /// Gets or sets the highlight colour behind the text.
    /// </summary>
    public string Highlight { get; set; }

    /// <summary>
    /// Gets or sets the horizontal alignment: <c>left</c>, <c>center</c>, <c>right</c> or <c>justify</c>.
    /// </summary>
    public string Alignment { get; set; }

    /// <summary>
    /// Gets or sets where text sits vertically in its box: <c>top</c>, <c>middle</c> or <c>bottom</c>.
    /// </summary>
    public string VerticalAlignment { get; set; }

    /// <summary>
    /// Gets or sets the line spacing as a multiple of single spacing.
    /// </summary>
    public double? LineSpacing { get; set; }

    /// <summary>
    /// Gets or sets the space above each paragraph, in points.
    /// </summary>
    public double? SpaceBefore { get; set; }

    /// <summary>
    /// Gets or sets the space below each paragraph, in points.
    /// </summary>
    public double? SpaceAfter { get; set; }

    /// <summary>
    /// Gets or sets how the text is capitalised: <c>none</c>, <c>all</c> or <c>small</c>.
    /// </summary>
    public string Capitalization { get; set; }

    /// <summary>
    /// Gets or sets what happens when the text does not fit: <c>none</c>, <c>shrink</c> or <c>resize</c>.
    /// </summary>
    public string AutoFit { get; set; }

    /// <summary>
    /// Gets or sets whether text wraps at the edge of its box.
    /// </summary>
    public bool? Wrap { get; set; }

    /// <summary>
    /// Gets a value indicating whether the style sets anything at all.
    /// </summary>
    public bool IsEmpty =>
        Font is null && Size is null && Bold is null && Italic is null && Underline is null &&
        Strikethrough is null && Color is null && Highlight is null && Alignment is null &&
        VerticalAlignment is null && LineSpacing is null && SpaceBefore is null && SpaceAfter is null &&
        Capitalization is null && AutoFit is null && Wrap is null;

    /// <summary>
    /// Layers another style on top of this one: every property the other style sets wins.
    /// </summary>
    /// <param name="overrides">The style laid on top, or <see langword="null"/> for none.</param>
    /// <returns>A new style holding the combined properties.</returns>
    public PresentationTextStyle Merge(PresentationTextStyle overrides)
    {
        if (overrides is null)
        {
            return Clone();
        }

        return new PresentationTextStyle
        {
            Font = overrides.Font ?? Font,
            Size = overrides.Size ?? Size,
            Bold = overrides.Bold ?? Bold,
            Italic = overrides.Italic ?? Italic,
            Underline = overrides.Underline ?? Underline,
            Strikethrough = overrides.Strikethrough ?? Strikethrough,
            Color = overrides.Color ?? Color,
            Highlight = overrides.Highlight ?? Highlight,
            Alignment = overrides.Alignment ?? Alignment,
            VerticalAlignment = overrides.VerticalAlignment ?? VerticalAlignment,
            LineSpacing = overrides.LineSpacing ?? LineSpacing,
            SpaceBefore = overrides.SpaceBefore ?? SpaceBefore,
            SpaceAfter = overrides.SpaceAfter ?? SpaceAfter,
            Capitalization = overrides.Capitalization ?? Capitalization,
            AutoFit = overrides.AutoFit ?? AutoFit,
            Wrap = overrides.Wrap ?? Wrap,
        };
    }

    /// <summary>
    /// Copies the style.
    /// </summary>
    /// <returns>The copy.</returns>
    public PresentationTextStyle Clone()
    {
        return (PresentationTextStyle)MemberwiseClone();
    }

    /// <summary>
    /// Layers two styles, either of which may be missing.
    /// </summary>
    /// <param name="baseStyle">The style underneath.</param>
    /// <param name="overrides">The style laid on top.</param>
    /// <returns>The combined style, or <see langword="null"/> when neither is set.</returns>
    public static PresentationTextStyle Combine(PresentationTextStyle baseStyle, PresentationTextStyle overrides)
    {
        if (baseStyle is null)
        {
            return overrides?.Clone();
        }

        return baseStyle.Merge(overrides);
    }
}
