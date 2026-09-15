namespace CrestApps.Core.AI.Documents.Generation.Spreadsheets;

/// <summary>
/// The visual style applied to a cell, a column of cells, or a header row. Every member is optional;
/// properties left unset inherit the spreadsheet application's default appearance.
/// </summary>
public sealed class SpreadsheetCellStyle
{
    /// <summary>
    /// Gets or sets a value indicating whether the text is bold.
    /// </summary>
    public bool? Bold { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the text is italic.
    /// </summary>
    public bool? Italic { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the text is underlined.
    /// </summary>
    public bool? Underline { get; set; }

    /// <summary>
    /// Gets or sets the font size, in points.
    /// </summary>
    public double? FontSize { get; set; }

    /// <summary>
    /// Gets or sets the font name, for example <c>Calibri</c>.
    /// </summary>
    public string FontName { get; set; }

    /// <summary>
    /// Gets or sets the text color as a hex string, for example <c>#1F4E79</c>.
    /// </summary>
    public string FontColor { get; set; }

    /// <summary>
    /// Gets or sets the cell background (fill) color as a hex string, for example <c>#DDEBF7</c>.
    /// </summary>
    public string BackgroundColor { get; set; }

    /// <summary>
    /// Gets or sets the horizontal alignment.
    /// </summary>
    public SpreadsheetHorizontalAlignment Alignment { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether text wraps within the cell.
    /// </summary>
    public bool? WrapText { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a thin border is drawn around the cell.
    /// </summary>
    public bool? Border { get; set; }

    /// <summary>
    /// Gets a value indicating whether this style would change a cell's appearance. A style with no
    /// populated member is skipped so it does not create a redundant cell format.
    /// </summary>
    public bool IsEmpty =>
        Bold is null &&
        Italic is null &&
        Underline is null &&
        FontSize is null &&
        string.IsNullOrEmpty(FontName) &&
        string.IsNullOrEmpty(FontColor) &&
        string.IsNullOrEmpty(BackgroundColor) &&
        Alignment == SpreadsheetHorizontalAlignment.General &&
        WrapText is null &&
        Border is null;
}
