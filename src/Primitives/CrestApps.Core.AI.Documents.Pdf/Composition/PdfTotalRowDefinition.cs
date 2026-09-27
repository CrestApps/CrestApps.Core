namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// A total row printed under a table's data, computed from the rows the table holds.
/// </summary>
internal sealed class PdfTotalRowDefinition
{
    /// <summary>
    /// Gets or sets the label printed in the first column. Defaults to <c>Total</c>.
    /// </summary>
    public string Label { get; set; }

    /// <summary>
    /// Gets or sets the function computed for each column, keyed by column heading: <c>sum</c>,
    /// <c>average</c>, <c>count</c>, <c>min</c> or <c>max</c>.
    /// </summary>
    public Dictionary<string, string> Functions { get; set; }
}
