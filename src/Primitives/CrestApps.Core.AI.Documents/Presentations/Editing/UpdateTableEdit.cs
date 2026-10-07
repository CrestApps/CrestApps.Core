namespace CrestApps.Core.AI.Documents.Presentations.Editing;

/// <summary>
/// Changes a slide table: its cells, its rows and columns, and how it looks.
/// </summary>
/// <remarks>
/// Row and column numbers refer to the table as it stands before the edit. Deletions are applied first, from
/// the end backwards, then insertions, then cell changes, so the numbers a caller read off the table stay
/// correct for every part of one edit.
/// </remarks>
public sealed class UpdateTableEdit : PresentationEdit
{
    /// <summary>
    /// Gets or sets the number of the slide.
    /// </summary>
    public int Slide { get; set; }

    /// <summary>
    /// Gets or sets the table, by identifier or name. The only table on the slide is used when this is not
    /// set.
    /// </summary>
    public string Element { get; set; }

    /// <summary>
    /// Gets or sets rows that replace the table's whole content, header included, keeping its position.
    /// </summary>
    public IList<IList<string>> ReplaceRows { get; set; }

    /// <summary>
    /// Gets or sets the rows to remove, counting from 1.
    /// </summary>
    public IList<int> DeleteRows { get; set; } = [];

    /// <summary>
    /// Gets or sets the columns to remove, counting from 1.
    /// </summary>
    public IList<int> DeleteColumns { get; set; } = [];

    /// <summary>
    /// Gets or sets rows to add. Each row is inserted after <see cref="InsertRowsAfter"/>.
    /// </summary>
    public IList<IList<string>> InsertRows { get; set; } = [];

    /// <summary>
    /// Gets or sets the row new rows follow, counting from 1; 0 puts them first. They are added at the end
    /// when this is not set.
    /// </summary>
    public int? InsertRowsAfter { get; set; }

    /// <summary>
    /// Gets or sets columns to add, each given as its cell text from the top row down.
    /// </summary>
    public IList<IList<string>> InsertColumns { get; set; } = [];

    /// <summary>
    /// Gets or sets the column new columns follow, counting from 1; 0 puts them first. They are added at the
    /// right when this is not set.
    /// </summary>
    public int? InsertColumnsAfter { get; set; }

    /// <summary>
    /// Gets or sets changes to individual cells.
    /// </summary>
    public IList<PresentationTableCellEdit> Cells { get; set; } = [];

    /// <summary>
    /// Gets or sets new column widths, left to right.
    /// </summary>
    public IList<PresentationLength?> ColumnWidths { get; set; } = [];

    /// <summary>
    /// Gets or sets whether the first row is styled as a header.
    /// </summary>
    public bool? HeaderRow { get; set; }

    /// <summary>
    /// Gets or sets how the whole table looks.
    /// </summary>
    public PresentationTableStyle Style { get; set; }
}
