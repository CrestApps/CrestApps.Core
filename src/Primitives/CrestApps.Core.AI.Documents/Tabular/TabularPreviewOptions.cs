namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Options that bound a tabular preview to something a reader can take in at a glance.
/// </summary>
/// <remarks>
/// A preview is a look at the data, not a copy of it. Every limit here exists because the alternative is a
/// picture so large that the chat surface scales it down to an unreadable smear, so the caps are applied
/// even when the caller asks for more and the preview says what it left out.
/// </remarks>
public sealed class TabularPreviewOptions
{
    /// <summary>
    /// Gets or sets the maximum number of data rows shown in a preview. Default is 50.
    /// </summary>
    public int MaxRows { get; set; } = 50;

    /// <summary>
    /// Gets or sets the maximum number of columns shown in a preview. Default is 15.
    /// </summary>
    public int MaxColumns { get; set; } = 15;

    /// <summary>
    /// Gets or sets the maximum number of characters shown for a single cell before the value is
    /// clipped with an ellipsis. Default is 32.
    /// </summary>
    public int MaxCellCharacters { get; set; } = 32;

    /// <summary>
    /// Gets or sets the maximum number of tables previewed when the caller names none. Default is 4.
    /// </summary>
    public int MaxTables { get; set; } = 4;

    /// <summary>
    /// Gets or sets the widest the drawn grid may be, in pixels. Columns that do not fit are dropped and
    /// reported rather than drawn off the edge. Default is 1100.
    /// </summary>
    public int MaxImageWidth { get; set; } = 1100;
}
