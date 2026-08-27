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
public static partial class TabularWorksheetShaper
{
    /// <summary>
    /// The name of the synthetic flag column appended to a table when embedded subtotal/total rows are
    /// detected. The column holds <c>1</c> for a suspected rollup row and <c>0</c> for a data row, so
    /// aggregate queries can exclude rollups with <c>WHERE is_subtotal = 0</c>.
    /// </summary>
    public const string SubtotalColumnName = "is_subtotal";

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
    /// Determines whether a data row is an embedded subtotal/total rollup rather than a genuine record.
    /// A row qualifies when it carries a total-style label (for example <c>Totals:</c> or
    /// <c>Waco Total</c>) alongside at least one numeric value, which distinguishes a rollup line from
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

    // Matches a leading "total"/"subtotal"/"grand total" (as in "Totals:") or a trailing one (as in
    // "Waco Total"). Anchored to word boundaries so a substring like "Totally Fun Inc" does not match.
    [GeneratedRegex(@"(^\s*(grand\s+)?(sub[-\s]?)?totals?\b|\btotals?\s*:?\s*$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TotalLabelRegex();
}
