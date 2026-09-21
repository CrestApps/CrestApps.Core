using System.Globalization;
using CrestApps.Core.AI.Documents.Generation.Spreadsheets;
using Cysharp.Text;

namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// One column of a tabular preview, as the reader meets it.
/// </summary>
/// <param name="Header">The heading printed above the column.</param>
/// <param name="IsNumeric">Whether the column holds numbers, which is what makes it right-aligned.</param>
internal readonly record struct TabularPreviewColumn(string Header, bool IsNumeric);

/// <summary>
/// A bounded, display-ready window onto tabular data: the first rows and columns, with every cell already
/// clipped to a length that fits a grid.
/// </summary>
/// <remarks>
/// The shaping happens once, here, so the drawn preview and the written-out one show the same window of the
/// same data. A preview that dropped different rows depending on how it was rendered would be worse than no
/// preview at all, because the reader has no way to tell which one they were handed.
/// </remarks>
internal sealed class TabularPreviewGrid
{
    private TabularPreviewGrid(
        string title,
        string subtitle,
        IReadOnlyList<TabularPreviewColumn> columns,
        IReadOnlyList<string[]> rows,
        long totalRowCount,
        int totalColumnCount,
        SpreadsheetFormatting formatting)
    {
        Title = title;
        Subtitle = subtitle;
        Columns = columns;
        Rows = rows;
        TotalRowCount = totalRowCount;
        TotalColumnCount = totalColumnCount;
        Formatting = formatting;
    }

    /// <summary>
    /// Gets the heading above the grid, usually the worksheet or table name.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the line under the title naming where the data came from, when there is one.
    /// </summary>
    public string Subtitle { get; }

    /// <summary>
    /// Gets the columns kept for display, in order.
    /// </summary>
    public IReadOnlyList<TabularPreviewColumn> Columns { get; }

    /// <summary>
    /// Gets the rows kept for display. Each row is aligned to <see cref="Columns"/> and every cell is
    /// already clipped.
    /// </summary>
    public IReadOnlyList<string[]> Rows { get; }

    /// <summary>
    /// Gets the number of rows the underlying data holds, which is what the preview is a window onto.
    /// </summary>
    public long TotalRowCount { get; }

    /// <summary>
    /// Gets the number of columns the underlying data holds.
    /// </summary>
    public int TotalColumnCount { get; }

    /// <summary>
    /// Gets the formatting the exported workbook is written with, or <see langword="null"/> when none is
    /// recorded.
    /// </summary>
    /// <remarks>
    /// Carried so the drawn preview takes its header colour and row banding from the same description the
    /// file is written from, rather than from a palette of its own that the reader never sees again.
    /// </remarks>
    public SpreadsheetFormatting Formatting { get; }

    /// <summary>
    /// Builds the bounded window onto a result set.
    /// </summary>
    /// <param name="title">The heading above the grid.</param>
    /// <param name="subtitle">The line naming the source, or <see langword="null"/>.</param>
    /// <param name="headers">The column headings, in result order.</param>
    /// <param name="rows">The result rows, each aligned to <paramref name="headers"/>.</param>
    /// <param name="totalRowCount">
    /// The number of rows the underlying data holds. Pass anything lower than the number of rows supplied
    /// and the rows on hand are reported as the whole of it.
    /// </param>
    /// <param name="declaredTypes">
    /// The SQLite storage type of each column, when the caller knows it. Where it is missing the type is
    /// inferred from the values on hand, which is enough to decide alignment.
    /// </param>
    /// <param name="options">The preview limits.</param>
    /// <param name="formatting">
    /// The formatting the exported workbook is written with, or <see langword="null"/>. Each column's
    /// values are presented the way the file presents them, so the picture and the download agree.
    /// </param>
    /// <returns>The shaped grid.</returns>
    public static TabularPreviewGrid Create(
        string title,
        string subtitle,
        IReadOnlyList<string> headers,
        IReadOnlyList<object[]> rows,
        long totalRowCount,
        IReadOnlyList<string> declaredTypes,
        TabularPreviewOptions options,
        SpreadsheetFormatting formatting = null)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(options);

        var columnCount = Math.Min(headers.Count, Math.Max(1, options.MaxColumns));
        var rowCount = Math.Min(rows.Count, Math.Max(1, options.MaxRows));
        var maxCellCharacters = Math.Max(4, options.MaxCellCharacters);

        // Looked up once per column rather than once per cell: a column's format does not vary down the
        // column, and a preview of the row cap is thousands of lookups.
        var columnFormats = new SpreadsheetColumnFormat[columnCount];

        if (formatting is not null)
        {
            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                var header = columnIndex < headers.Count ? headers[columnIndex] : null;

                columnFormats[columnIndex] = string.IsNullOrWhiteSpace(header)
                    ? null
                    : formatting.FindColumn(header);
            }
        }

        var cells = new List<string[]>(rowCount);

        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var source = rows[rowIndex];
            var row = new string[columnCount];

            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                var value = source is not null && columnIndex < source.Length
                    ? TabularPreviewValueFormat.Format(source[columnIndex], columnFormats[columnIndex])
                    : string.Empty;

                row[columnIndex] = Clip(value, maxCellCharacters);
            }

            cells.Add(row);
        }

        var columns = new List<TabularPreviewColumn>(columnCount);

        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            var declaredType = declaredTypes is not null && columnIndex < declaredTypes.Count
                ? declaredTypes[columnIndex]
                : null;

            columns.Add(new TabularPreviewColumn(
                Clip(headers[columnIndex] ?? string.Empty, maxCellCharacters),
                IsNumericColumn(declaredType, cells, columnIndex)));
        }

        return new TabularPreviewGrid(
            title,
            subtitle,
            columns,
            cells,
            Math.Max(totalRowCount, cells.Count),
            headers.Count,
            formatting);
    }

    /// <summary>
    /// Describes what the reader is looking at and, plainly, what was left out of it.
    /// </summary>
    /// <param name="shownColumnCount">
    /// How many columns were actually shown. This can be fewer than <see cref="Columns"/> holds, because a
    /// renderer working to a width drops the columns that do not fit.
    /// </param>
    /// <returns>The caption printed under the grid.</returns>
    public string BuildCaption(int shownColumnCount)
    {
        using var builder = ZString.CreateStringBuilder();

        builder.Append("Showing ");
        builder.Append(Rows.Count.ToString("N0", CultureInfo.InvariantCulture));

        if (TotalRowCount > Rows.Count)
        {
            builder.Append(" of ");
            builder.Append(TotalRowCount.ToString("N0", CultureInfo.InvariantCulture));
        }

        builder.Append(TotalRowCount == 1 ? " row" : " rows");
        builder.Append(" and ");
        builder.Append(shownColumnCount.ToString("N0", CultureInfo.InvariantCulture));

        if (TotalColumnCount > shownColumnCount)
        {
            builder.Append(" of ");
            builder.Append(TotalColumnCount.ToString("N0", CultureInfo.InvariantCulture));
        }

        builder.Append(TotalColumnCount == 1 ? " column" : " columns");

        return builder.ToString();
    }

    /// <summary>
    /// Clips a value to a length a grid cell can hold, marking that it was clipped.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="maxCharacters">The budget.</param>
    /// <returns>The value, or as much of it as fits followed by an ellipsis.</returns>
    private static string Clip(string value, int maxCharacters)
    {
        // A newline or a tab is one character that takes a whole line or a whole column once it is drawn,
        // so they are folded to spaces before the budget is applied rather than after it.
        var flattened = value.AsSpan().IndexOfAny('\r', '\n', '\t') >= 0
            ? value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ')
            : value;

        return flattened.Length <= maxCharacters
            ? flattened
            : string.Concat(flattened.AsSpan(0, maxCharacters - 1).TrimEnd(), "…");
    }

    /// <summary>
    /// Decides whether a column reads as numbers, which is what makes it right-aligned the way a
    /// spreadsheet shows it.
    /// </summary>
    /// <param name="declaredType">The storage type the workspace declared, when it is known.</param>
    /// <param name="rows">The shaped rows.</param>
    /// <param name="columnIndex">The column.</param>
    /// <returns><see langword="true"/> when the column should be right-aligned.</returns>
    private static bool IsNumericColumn(string declaredType, List<string[]> rows, int columnIndex)
    {
        if (declaredType is "INTEGER" or "REAL")
        {
            return true;
        }

        if (declaredType is "TEXT")
        {
            return false;
        }

        // Nothing was declared, which is the case for a query result. Every value present has to parse as a
        // number, so a single label anywhere in the column keeps the whole column left-aligned.
        var sawValue = false;

        foreach (var row in rows)
        {
            var value = row[columnIndex];

            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                return false;
            }

            sawValue = true;
        }

        return sawValue;
    }
}
