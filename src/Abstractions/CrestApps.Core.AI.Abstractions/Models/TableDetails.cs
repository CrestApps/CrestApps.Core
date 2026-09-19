namespace CrestApps.Core.AI.Models;

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
