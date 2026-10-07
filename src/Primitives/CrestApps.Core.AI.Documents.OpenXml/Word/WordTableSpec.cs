using CrestApps.Core.AI.Documents.Generation.RichText;
using CrestApps.Core.AI.Documents.Generation.Spreadsheets;

namespace CrestApps.Core.AI.Documents.OpenXml.Word;

/// <summary>
/// Describes a table to write: its columns, its rows and how it looks.
/// </summary>
internal sealed class WordTableSpec
{
    /// <summary>
    /// Gets or sets the columns. When a column has a header and <see cref="HeaderRow"/> is set, the headers
    /// form the first row.
    /// </summary>
    public IList<WordTableColumn> Columns { get; set; } = [];

    /// <summary>
    /// Gets or sets the body rows.
    /// </summary>
    public IList<IList<WordTableCell>> Rows { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the column headers are written as a header row.
    /// </summary>
    public bool HeaderRow { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the header row repeats at the top of every page the table
    /// runs onto.
    /// </summary>
    public bool RepeatHeaderRow { get; set; } = true;

    /// <summary>
    /// Gets or sets the look: <c>data</c> (shaded header, banded rows), <c>grid</c> (plain grid), <c>light</c>
    /// (horizontal rules only), <c>plain</c> (no borders), or the name of one of the document's table styles.
    /// </summary>
    public string Style { get; set; }

    /// <summary>
    /// Gets or sets whether every other row is shaded, or <see langword="null"/> to follow the style.
    /// </summary>
    public bool? Banded { get; set; }

    /// <summary>
    /// Gets or sets the header row fill, overriding the style's.
    /// </summary>
    public string HeaderFill { get; set; }

    /// <summary>
    /// Gets or sets the header row text color, overriding the style's.
    /// </summary>
    public string HeaderTextColor { get; set; }

    /// <summary>
    /// Gets or sets the border color, overriding the style's.
    /// </summary>
    public string BorderColor { get; set; }

    /// <summary>
    /// Gets or sets the fill of banded rows, overriding the style's.
    /// </summary>
    public string BandFill { get; set; }

    /// <summary>
    /// Gets or sets the text size in points, or <see langword="null"/> for the body size.
    /// </summary>
    public double? FontSize { get; set; }

    /// <summary>
    /// Gets or sets the table width: a length, a percentage of the text width, or <c>auto</c>. Defaults to the
    /// full text width.
    /// </summary>
    public string Width { get; set; }

    /// <summary>
    /// Gets or sets how the table sits between the margins: <c>left</c>, <c>center</c> or <c>right</c>.
    /// </summary>
    public string Alignment { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the first column is bold, as row labels often are.
    /// </summary>
    public bool BoldFirstColumn { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether headers and cell text are written exactly as given rather than
    /// read as inline Markdown. Values taken from a spreadsheet are data, so a backslash in a path, an asterisk
    /// in a pattern or the underscores of <c>__init__</c> are part of the value, not markup.
    /// </summary>
    public bool Literal { get; set; }
}

/// <summary>
/// One column of a table.
/// </summary>
internal sealed class WordTableColumn
{
    /// <summary>
    /// Gets or sets the header text, with inline Markdown unless the table is <see cref="WordTableSpec.Literal"/>.
    /// </summary>
    public string Header { get; set; }

    /// <summary>
    /// Gets or sets the header as already parsed spans, written as they are. Takes precedence over the
    /// formatting of <see cref="Header"/>, which still has to hold the header's text.
    /// </summary>
    public IReadOnlyList<RichTextSpan> HeaderSpans { get; set; }

    /// <summary>
    /// Gets or sets the width: a length such as <c>1.5in</c>, a percentage of the table, or a relative weight.
    /// </summary>
    public string Width { get; set; }

    /// <summary>
    /// Gets or sets the alignment of the column's cells: <c>left</c>, <c>center</c> or <c>right</c>. Numeric
    /// columns are right-aligned unless told otherwise.
    /// </summary>
    public string Alignment { get; set; }

    /// <summary>
    /// Gets or sets how the column's values are presented, as the spreadsheet export would present them.
    /// </summary>
    public SpreadsheetColumnFormat Format { get; set; }
}

/// <summary>
/// One cell of a table.
/// </summary>
internal sealed class WordTableCell
{
    /// <summary>
    /// Gets or sets the text, with inline Markdown unless the table is <see cref="WordTableSpec.Literal"/>.
    /// Ignored when <see cref="Value"/> or <see cref="Spans"/> is set.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Gets or sets the content as already parsed spans, written as they are, so text that was parsed once
    /// is not parsed again. Ignored when <see cref="Value"/> is set.
    /// </summary>
    public IReadOnlyList<RichTextSpan> Spans { get; set; }

    /// <summary>
    /// Gets or sets a raw value, presented with the column's format.
    /// </summary>
    public object Value { get; set; }

    /// <summary>
    /// Gets or sets how the cell's value is presented, overriding the column's format, such as a count under
    /// a currency column that is shown as a whole number.
    /// </summary>
    public SpreadsheetColumnFormat Format { get; set; }

    /// <summary>
    /// Gets or sets whether the cell is bold.
    /// </summary>
    public bool? Bold { get; set; }

    /// <summary>
    /// Gets or sets whether the cell is italic.
    /// </summary>
    public bool? Italic { get; set; }

    /// <summary>
    /// Gets or sets the text color.
    /// </summary>
    public string Color { get; set; }

    /// <summary>
    /// Gets or sets the cell fill.
    /// </summary>
    public string Fill { get; set; }

    /// <summary>
    /// Gets or sets the alignment, overriding the column's.
    /// </summary>
    public string Alignment { get; set; }

    /// <summary>
    /// Gets or sets the vertical alignment: <c>top</c>, <c>center</c> or <c>bottom</c>.
    /// </summary>
    public string VerticalAlignment { get; set; }

    /// <summary>
    /// Gets or sets how many columns the cell spans.
    /// </summary>
    public int ColumnSpan { get; set; } = 1;

    /// <summary>
    /// Gets or sets how many rows the cell spans.
    /// </summary>
    public int RowSpan { get; set; } = 1;

    /// <summary>
    /// Creates a cell holding text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The cell.</returns>
    public static WordTableCell FromText(string text)
    {
        return new WordTableCell { Text = text };
    }
}
