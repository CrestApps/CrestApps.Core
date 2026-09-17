namespace CrestApps.Core.AI.Documents.Generation.Spreadsheets;

/// <summary>
/// A conditional formatting rule applied to one column's data cells.
/// </summary>
public sealed class SpreadsheetConditionalFormat
{
    /// <summary>
    /// Gets or sets the column the rule applies to, matched against the generated header row.
    /// </summary>
    public string Column { get; set; }

    /// <summary>
    /// Gets or sets the rule kind.
    /// </summary>
    public SpreadsheetConditionalRule Rule { get; set; }

    /// <summary>
    /// Gets or sets the comparison value used by the comparison rules.
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    /// Gets or sets the upper bound used by <see cref="SpreadsheetConditionalRule.Between"/>.
    /// </summary>
    public string SecondValue { get; set; }

    /// <summary>
    /// Gets or sets the style applied to cells the rule matches. Used by the comparison rules and by
    /// <see cref="SpreadsheetConditionalRule.DuplicateValues"/>.
    /// </summary>
    public SpreadsheetCellStyle Style { get; set; }

    /// <summary>
    /// Gets or sets the gradient color for the lowest value, used by
    /// <see cref="SpreadsheetConditionalRule.ColorScale"/>. Defaults to a red.
    /// </summary>
    public string MinimumColor { get; set; }

    /// <summary>
    /// Gets or sets the gradient color for the midpoint. When set, the color scale uses three stops
    /// instead of two. Defaults to unset.
    /// </summary>
    public string MidpointColor { get; set; }

    /// <summary>
    /// Gets or sets the gradient color for the highest value, used by
    /// <see cref="SpreadsheetConditionalRule.ColorScale"/>. Defaults to a green.
    /// </summary>
    public string MaximumColor { get; set; }

    /// <summary>
    /// Gets or sets the bar color used by <see cref="SpreadsheetConditionalRule.DataBar"/>.
    /// </summary>
    public string BarColor { get; set; }

    /// <summary>
    /// Gets or sets the icon set name used by <see cref="SpreadsheetConditionalRule.IconSet"/>, for
    /// example <c>3TrafficLights1</c> or <c>3Arrows</c>.
    /// </summary>
    public string IconSet { get; set; }
}
