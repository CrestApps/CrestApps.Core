namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// One column of a table: its heading, its width, and how its values are presented.
/// </summary>
internal sealed class PdfTableColumnDefinition
{
    /// <summary>
    /// Gets or sets the column heading.
    /// </summary>
    public string Header { get; set; }

    /// <summary>
    /// Gets or sets the width as a percentage of the table width. Measured from the content when omitted.
    /// </summary>
    public double? Width { get; set; }

    /// <summary>
    /// Gets or sets the alignment: <c>left</c>, <c>center</c> or <c>right</c>. Numbers are right-aligned
    /// when this is omitted.
    /// </summary>
    public string Align { get; set; }

    /// <summary>
    /// Gets or sets the value presentation: <c>general</c>, <c>text</c>, <c>number</c>, <c>integer</c>,
    /// <c>currency</c>, <c>accounting</c>, <c>percent</c>, <c>date</c>, <c>datetime</c>, <c>time</c> or
    /// <c>scientific</c>.
    /// </summary>
    public string Format { get; set; }

    /// <summary>
    /// Gets or sets the number of decimal places.
    /// </summary>
    public int? Decimals { get; set; }

    /// <summary>
    /// Gets or sets the currency symbol, for example <c>$</c> or <c>€</c>.
    /// </summary>
    public string CurrencySymbol { get; set; }

    /// <summary>
    /// Gets or sets an explicit spreadsheet format code, such as <c>#,##0.00</c>, overriding
    /// <see cref="Format"/>.
    /// </summary>
    public string FormatCode { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether negative numbers are printed in red.
    /// </summary>
    public bool? NegativesInRed { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the column's values are bold.
    /// </summary>
    public bool? Bold { get; set; }

    /// <summary>
    /// Gets or sets the column's text colour.
    /// </summary>
    public string Color { get; set; }

    /// <summary>
    /// Gets or sets the column's fill colour.
    /// </summary>
    public string BackgroundColor { get; set; }
}
