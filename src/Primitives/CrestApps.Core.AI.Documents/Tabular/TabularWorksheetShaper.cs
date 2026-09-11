using System.Globalization;
using System.Text.RegularExpressions;

namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Shapes a raw worksheet (a stream of non-empty rows) into a clean table by locating the real header
/// row, widening the header to cover populated cells that have no header, and recognizing embedded
/// subtotal/total rows. The logic is shared by every import path (the streaming Open XML importer, the
/// in-memory artifact/CSV path, and the metadata tools) so a workbook is interpreted the same way no
/// matter which path loads it.
/// </summary>
/// <remarks>
/// <para>
/// Terminology used throughout this type, because the distinction decides whether a total is right:
/// </para>
/// <para>
/// A <strong>rollup row</strong> (also called a subtotal, total, or grand-total row) is a row whose
/// values aggregate <em>other rows of the same table</em> -- a "Region Total" line printed beneath the
/// rows it covers, or a grand total printed beneath those. It restates figures that are already
/// present, so leaving it among them makes a plain <c>SUM</c> down the column count every underlying
/// row at least twice. On a sheet with per-group subtotals and a grand total over them, the result is
/// three times the true figure. These rows are what this type detects and what the importers move into
/// a separate table.
/// </para>
/// <para>
/// A <strong>row total column</strong> (or line-total column) is the opposite arrangement and is
/// harmless: a column whose value aggregates <em>other columns within its own row</em> -- a "Total"
/// column adding that record's components together. Every row still represents one record, so summing
/// that column down the rows is exactly correct. Nothing here treats such a column as a rollup, and
/// there is deliberately no notion of a "rollup column": duplication only arises along the axis a
/// <c>SUM</c> travels, which is rows.
/// </para>
/// </remarks>
public static partial class TabularWorksheetShaper
{
    /// <summary>
    /// The suffix appended to a table's name to form the sibling table that holds its rollup rows.
    /// </summary>
    public const string RollupTableSuffix = "_rollups";

    /// <summary>
    /// Builds the name of the sibling table that holds <paramref name="tableName"/>'s rollup rows.
    /// </summary>
    /// <remarks>
    /// Subtotal and grand-total rows are aggregates of other rows, so keeping them in the same table as
    /// the rows they summarize makes the obvious query wrong: a plain <c>SUM</c> over the column counts
    /// every underlying row once as itself and again inside each rollup that covers it. Excluding them
    /// by convention -- a flag column the caller is asked to filter on -- fails whenever the filter is
    /// forgotten, and the result looks entirely plausible when it happens. Separating the two grains
    /// into separate tables makes the default query correct instead, and keeps the sheet's own totals
    /// available here to reconcile against.
    /// </remarks>
    /// <param name="tableName">The data table's name.</param>
    /// <returns>The sibling rollup table's name.</returns>
    public static string GetRollupTableName(string tableName)
    {
        return string.Concat(tableName, RollupTableSuffix);
    }

    /// <summary>
    /// The maximum number of leading rows examined when locating the header row. Title banners and
    /// notes almost always sit within the first few rows, so scanning past this adds cost without
    /// improving accuracy.
    /// </summary>
    public const int HeaderScanRows = 10;

    /// <summary>
    /// Selects the index of the header row among the supplied leading rows, skipping title/banner rows
    /// that sit above it. The chosen row is the earliest one (within the scan window) that carries the
    /// most textual labels; when no row carries any label the first row is used, preserving the legacy
    /// "first non-empty row is the header" behavior.
    /// </summary>
    /// <param name="leadingRows">The first non-empty rows of the worksheet, in order.</param>
    /// <returns>The zero-based index of the header row within <paramref name="leadingRows"/>.</returns>
    public static int DetectHeaderRowIndex(IReadOnlyList<IReadOnlyList<string>> leadingRows)
    {
        if (leadingRows is null || leadingRows.Count == 0)
        {
            return 0;
        }

        var scan = Math.Min(leadingRows.Count, HeaderScanRows);
        var bestIndex = 0;
        var bestScore = -1;

        for (var i = 0; i < scan; i++)
        {
            // A strictly-greater comparison keeps the earliest row on ties, so a genuine header on the
            // first row is never displaced by an equally-labelled data row beneath it.
            var score = CountLabelCells(leadingRows[i]);

            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        return bestScore <= 0 ? 0 : bestIndex;
    }

    public static List<string> FixDuplicateColumnNames(IReadOnlyList<IReadOnlyList<string>> leadingRows, int headerRowIndex)
    {
        if (leadingRows is null || headerRowIndex < 0 || headerRowIndex >= leadingRows.Count)
        {
            return [];
        }

        var header = leadingRows[headerRowIndex];

        var result = header is null ? [] : new List<string>(header);

        // The header is the first row, so there is no band above it to borrow from.
        if (headerRowIndex == 0)
        {
            return result;
        }

        var duplicates = GetDuplicateLabels(result);

        if (duplicates.Count == 0)
        {
            return result;
        }

        IReadOnlyList<string> bandRow = null;

        for (var i = headerRowIndex - 1; i >= 0; i--)
        {
            if (CountPopulatedCells(leadingRows[i]) > 0)
            {
                bandRow = leadingRows[i];

                break;
            }
        }

        if (bandRow is null)
        {
            return result;
        }

        var bandPopulated = CountPopulatedCells(bandRow);

        if (bandPopulated < 2 || bandPopulated >= CountPopulatedCells(result))
        {
            return result;
        }

        var band = ForwardFill(bandRow, result.Count);

        for (var i = 0; i < result.Count; i++)
        {
            var label = result[i];

            // Unique labels keep the name they already have, so only the ambiguous ones change.
            if (string.IsNullOrWhiteSpace(label) || !duplicates.Contains(label.Trim()))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(band[i]))
            {
                continue;
            }

            result[i] = $"{label.Trim()} {band[i]}";
        }

        return result;
    }

    /// <summary>
    /// Widens <paramref name="header"/> so it covers the widest data row, padding with empty entries.
    /// Populated cells that extend past the last header therefore become real (auto-named) columns
    /// instead of being silently dropped. Column naming is left to <see cref="TabularWorkspaceSqliteHelpers.BuildColumns(IReadOnlyList{string})"/>,
    /// which turns each padded blank into <c>column_N</c>.
    /// </summary>
    /// <param name="header">The detected header row.</param>
    /// <param name="dataRows">The data rows used to measure the true column count.</param>
    /// <returns>The header padded to the effective column count.</returns>
    public static List<string> ExpandHeader(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> dataRows)
    {
        ArgumentNullException.ThrowIfNull(header);

        var width = header.Count;

        if (dataRows is not null)
        {
            foreach (var row in dataRows)
            {
                if (row is not null && row.Count > width)
                {
                    width = row.Count;
                }
            }
        }

        var expanded = new List<string>(width);

        for (var i = 0; i < width; i++)
        {
            expanded.Add(i < header.Count ? header[i] : string.Empty);
        }

        return expanded;
    }

    /// <summary>
    /// Determines whether a data row is an embedded subtotal/total rollup rather than a genuine record,
    /// using the strongest evidence available.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A spreadsheet formula is definitive evidence and is preferred whenever the source format carries
    /// one. A rollup aggregates <em>other rows</em> of the same column (<c>=SUM(C2:C27)</c> on the row
    /// beneath them), so including it double-counts every row it covers. An ordinary record may also
    /// hold a <c>SUM</c>, but only across <em>its own</em> cells (<c>=SUM(G27,E27,H27)</c>) -- a
    /// cross-column line total, which sums correctly down a column and must be kept. That distinction
    /// is what <paramref name="hasVerticalAggregateFormula"/> carries; see
    /// <c>OpenXmlTabularWorksheetReader</c> for how it is derived.
    /// </para>
    /// <para>
    /// Delimited sources (CSV/TSV) and value-only workbooks have no formulas, so the label heuristic
    /// remains as a fallback: a total-style label alongside at least one numeric value.
    /// </para>
    /// </remarks>
    /// <param name="row">The data row to classify.</param>
    /// <param name="hasVerticalAggregateFormula">
    /// <see langword="true"/> when a cell in this row aggregates other rows of the sheet.
    /// </param>
    /// <returns><see langword="true"/> when the row is a subtotal/total rollup.</returns>
    public static bool IsSubtotalRow(IReadOnlyList<string> row, bool hasVerticalAggregateFormula)
    {
        return hasVerticalAggregateFormula || IsSubtotalRow(row);
    }

    /// <summary>
    /// Determines whether a data row is an embedded subtotal/total rollup rather than a genuine record.
    /// A row qualifies when it carries a total-style label (for example <c>Totals:</c> or
    /// <c>Rivertown Total</c>) alongside at least one numeric value, which distinguishes a rollup line from
    /// an ordinary row that merely happens to contain the word "total".
    /// </summary>
    /// <param name="row">The data row to classify.</param>
    /// <returns><see langword="true"/> when the row looks like a subtotal/total rollup.</returns>
    public static bool IsSubtotalRow(IReadOnlyList<string> row)
    {
        if (row is null)
        {
            return false;
        }

        var hasTotalLabel = false;
        var hasNumeric = false;

        foreach (var cell in row)
        {
            if (string.IsNullOrWhiteSpace(cell))
            {
                continue;
            }

            var trimmed = cell.Trim();

            if (IsNumericLike(trimmed))
            {
                hasNumeric = true;

                continue;
            }

            if (LooksLikeTotalLabel(trimmed))
            {
                hasTotalLabel = true;
            }
        }

        return hasTotalLabel && hasNumeric;
    }

    // Labels that appear more than once in the header, compared case-insensitively after trimming.
    private static HashSet<string> GetDuplicateLabels(IReadOnlyList<string> header)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cell in header)
        {
            if (string.IsNullOrWhiteSpace(cell))
            {
                continue;
            }

            if (!seen.Add(cell.Trim()))
            {
                duplicates.Add(cell.Trim());
            }
        }

        return duplicates;
    }

    private static int CountPopulatedCells(IReadOnlyList<string> row)
    {
        if (row is null)
        {
            return 0;
        }

        var count = 0;

        foreach (var cell in row)
        {
            if (!string.IsNullOrWhiteSpace(cell))
            {
                count++;
            }
        }

        return count;
    }

    // Carries each value rightward until the next one, reproducing the span of a merged banner cell.
    private static List<string> ForwardFill(IReadOnlyList<string> row, int width)
    {
        var filled = new List<string>(width);
        string current = null;

        for (var i = 0; i < width; i++)
        {
            var value = i < row.Count ? row[i] : null;

            if (!string.IsNullOrWhiteSpace(value))
            {
                current = value.Trim();
            }

            filled.Add(current);
        }

        return filled;
    }

    private static int CountLabelCells(IReadOnlyList<string> row)
    {
        if (row is null)
        {
            return 0;
        }

        var count = 0;

        foreach (var cell in row)
        {
            if (string.IsNullOrWhiteSpace(cell))
            {
                continue;
            }

            if (!IsNumericLike(cell.Trim()))
            {
                count++;
            }
        }

        return count;
    }

    // Recognizes values such as numbers, currency, percentages, thousands-separated amounts, and
    // accounting-style negatives in parentheses. Used only to tell labels apart from values, so it errs
    // toward treating anything number-shaped as a value.
    private static bool IsNumericLike(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var span = value.AsSpan().Trim();

        if (span.Length == 0)
        {
            return false;
        }

        Span<char> buffer = span.Length <= 64 ? stackalloc char[span.Length] : new char[span.Length];
        var length = 0;
        var hasDigit = false;

        foreach (var c in span)
        {
            switch (c)
            {
                case '$' or '%' or ',' or '(' or ')' or '+' or ' ':
                    continue;
                default:
                    if (char.IsAsciiDigit(c))
                    {
                        hasDigit = true;
                    }

                    buffer[length++] = c;

                    break;
            }
        }

        if (!hasDigit || length == 0)
        {
            return false;
        }

        return double.TryParse(buffer[..length], NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    private static bool LooksLikeTotalLabel(string value)
    {
        return TotalLabelRegex().IsMatch(value);
    }

    // Matches a label that is entirely a total word (as in "Totals:", "Grand Total", "Subtotal") or one
    // that ends in a total word (as in "Rivertown Total"). The leading form is deliberately anchored to the
    // whole label rather than to a word boundary: a real company name that merely begins with "Total"
    // -- "Total Wine & More", "Total Quality Logistics" -- is a data row, and treating it as a rollup
    // would move genuine revenue out of the table.
    [GeneratedRegex(@"^\s*((grand\s+)?(sub[-\s]?)?totals?\s*[:.]?\s*$|.*\btotals?\s*[:.]?\s*$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TotalLabelRegex();
}
