namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// A rule that colours a table cell by its value, the printed counterpart of a spreadsheet's conditional
/// formatting.
/// </summary>
internal sealed class PdfHighlightRuleDefinition
{
    /// <summary>
    /// Gets or sets the column heading the rule applies to.
    /// </summary>
    public string Column { get; set; }

    /// <summary>
    /// Gets or sets the comparison: <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>, <c>eq</c>, <c>ne</c>,
    /// <c>between</c>, <c>contains</c>, <c>negative</c>, <c>positive</c> or <c>scale</c> (a colour
    /// gradient from the column's lowest value to its highest).
    /// </summary>
    public string Operator { get; set; }

    /// <summary>
    /// Gets or sets the value compared against.
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    /// Gets or sets the upper bound of a <c>between</c> comparison.
    /// </summary>
    public string Value2 { get; set; }

    /// <summary>
    /// Gets or sets the fill colour of a matching cell, or the colour of the highest value on a scale.
    /// </summary>
    public string BackgroundColor { get; set; }

    /// <summary>
    /// Gets or sets the colour of the lowest value on a scale.
    /// </summary>
    public string MinimumColor { get; set; }

    /// <summary>
    /// Gets or sets the text colour of a matching cell.
    /// </summary>
    public string Color { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a matching cell is bold.
    /// </summary>
    public bool? Bold { get; set; }
}
