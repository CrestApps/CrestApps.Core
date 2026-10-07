namespace CrestApps.Core.AI.Documents.Presentations.Models;

/// <summary>
/// A table on a slide.
/// </summary>
public sealed class PresentationTable
{
    /// <summary>
    /// Gets or sets the width of each column, in EMUs.
    /// </summary>
    public IList<long> ColumnWidths { get; set; } = [];

    /// <summary>
    /// Gets or sets the height of each row, in EMUs.
    /// </summary>
    public IList<long> RowHeights { get; set; } = [];

    /// <summary>
    /// Gets or sets the cells, row by row.
    /// </summary>
    public IList<IList<PresentationTableCell>> Rows { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the first row is styled as a header.
    /// </summary>
    public bool FirstRow { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether alternate rows are shaded.
    /// </summary>
    public bool BandedRows { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the first column is emphasised.
    /// </summary>
    public bool FirstColumn { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the last row is styled as a total.
    /// </summary>
    public bool LastRow { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the table style the table is drawn with, when it names one.
    /// </summary>
    public string StyleId { get; set; }

    /// <summary>
    /// Gets the number of columns.
    /// </summary>
    public int ColumnCount => ColumnWidths.Count;

    /// <summary>
    /// Returns the text of every cell, row by row, for a reader that only wants the values.
    /// </summary>
    /// <returns>The cell text.</returns>
    public List<List<string>> ToText()
    {
        return Rows
            .Select(row => row.Select(cell => cell.IsMerged ? string.Empty : cell.Text.PlainText).ToList())
            .ToList();
    }
}
