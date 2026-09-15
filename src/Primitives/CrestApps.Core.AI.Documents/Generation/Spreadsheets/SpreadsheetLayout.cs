namespace CrestApps.Core.AI.Documents.Generation.Spreadsheets;

/// <summary>
/// Resolves <see cref="GeneratedFileContent"/> and its requested formatting into a concrete sheet plan:
/// the final column order, the storage type of every column, the number format codes, and the row
/// numbers the header, data, and total row occupy.
/// <para>
/// The resolution lives here rather than in the writer so the decisions that matter most — above all,
/// whether a column is stored as a real number or as text — are plain to read and testable without
/// opening a generated file.
/// </para>
/// </summary>
public sealed class SpreadsheetLayout
{
    private const double MinimumColumnWidth = 8;
    private const double MaximumColumnWidth = 60;
    private const double ColumnWidthPadding = 2.5;

    private readonly Dictionary<string, int> _columnsByName = new(StringComparer.OrdinalIgnoreCase);

    private SpreadsheetLayout(
        string sheetName,
        IReadOnlyList<SpreadsheetLayoutColumn> columns,
        IReadOnlyList<IReadOnlyList<string>> rows,
        SpreadsheetFormatting formatting)
    {
        SheetName = sheetName;
        Columns = columns;
        Rows = rows;
        Formatting = formatting;

        foreach (var column in columns)
        {
            // A duplicated header keeps its first position, matching how a formula reader resolves it.
            _columnsByName.TryAdd(column.Name?.Trim() ?? string.Empty, column.Index);
        }
    }

    /// <summary>
    /// Gets the worksheet name.
    /// </summary>
    public string SheetName { get; }

    /// <summary>
    /// Gets the resolved columns, in sheet order.
    /// </summary>
    public IReadOnlyList<SpreadsheetLayoutColumn> Columns { get; }

    /// <summary>
    /// Gets the data rows.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; }

    /// <summary>
    /// Gets the requested formatting. Never <see langword="null"/>; an absent spec is resolved to the
    /// defaults so callers do not have to null-check.
    /// </summary>
    public SpreadsheetFormatting Formatting { get; }

    /// <summary>
    /// The one-based row number of the header row. The header is always the first row of a generated
    /// sheet, so formulas and ranges can rely on it.
    /// </summary>
    public const int HeaderRowNumber = 1;

    /// <summary>
    /// The one-based row number of the first data row.
    /// </summary>
    public const int FirstDataRowNumber = 2;

    /// <summary>
    /// Gets the one-based row number of the last data row, or <see cref="HeaderRowNumber"/> when there
    /// is no data.
    /// </summary>
    public int LastDataRowNumber => Rows.Count == 0
        ? HeaderRowNumber
        : FirstDataRowNumber + Rows.Count - 1;

    /// <summary>
    /// Gets a value indicating whether a total row is appended below the data.
    /// </summary>
    public bool HasTotalRow => Formatting.TotalRow is not null && Rows.Count > 0;

    /// <summary>
    /// Gets the one-based row number of the total row.
    /// </summary>
    public int TotalRowNumber => LastDataRowNumber + 1;

    /// <summary>
    /// Resolves a column name to its zero-based index.
    /// </summary>
    /// <param name="name">The column name.</param>
    /// <param name="index">The resolved index.</param>
    /// <returns><see langword="true"/> when the column exists.</returns>
    public bool TryGetColumnIndex(string name, out int index)
    {
        index = -1;

        return !string.IsNullOrWhiteSpace(name) && _columnsByName.TryGetValue(name.Trim(), out index);
    }

    /// <summary>
    /// Finds a resolved column by name.
    /// </summary>
    /// <param name="name">The column name.</param>
    /// <returns>The column, or <see langword="null"/> when no column matches.</returns>
    public SpreadsheetLayoutColumn FindColumn(string name)
    {
        return TryGetColumnIndex(name, out var index)
            ? Columns[index]
            : null;
    }

    /// <summary>
    /// Builds the layout for the supplied content.
    /// </summary>
    /// <param name="content">The content being written.</param>
    /// <returns>The resolved layout.</returns>
    public static SpreadsheetLayout Create(GeneratedFileContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var formatting = content.SpreadsheetFormatting ?? new SpreadsheetFormatting();
        var header = content.Header ?? [];
        var rows = content.Rows ?? [];
        var columns = new List<SpreadsheetLayoutColumn>(header.Count);

        for (var index = 0; index < header.Count; index++)
        {
            var name = header[index] ?? string.Empty;
            var requested = formatting.FindColumn(name);

            columns.Add(Resolve(name, index, requested, rows, isComputed: false));
        }

        AppendComputedColumns(formatting, header, rows, columns);

        var sheetName = NormalizeSheetName(formatting.SheetName);

        return new SpreadsheetLayout(sheetName, columns, rows, formatting);
    }

    private static void AppendComputedColumns(
        SpreadsheetFormatting formatting,
        IReadOnlyList<string> header,
        IReadOnlyList<IReadOnlyList<string>> rows,
        List<SpreadsheetLayoutColumn> columns)
    {
        if (formatting.Columns is null)
        {
            return;
        }

        foreach (var requested in formatting.Columns)
        {
            if (requested is null ||
                string.IsNullOrWhiteSpace(requested.Column) ||
                string.IsNullOrWhiteSpace(requested.Formula))
            {
                continue;
            }

            // A format naming an existing header refines that column; one naming a new header adds it.
            if (MatchesExistingHeader(header, requested.Column))
            {
                continue;
            }

            columns.Add(Resolve(requested.Column.Trim(), columns.Count, requested, rows, isComputed: true));
        }
    }

    private static bool MatchesExistingHeader(IReadOnlyList<string> header, string name)
    {
        foreach (var existing in header)
        {
            if (SpreadsheetFormatting.NameMatches(existing, name))
            {
                return true;
            }
        }

        return false;
    }

    private static SpreadsheetLayoutColumn Resolve(
        string name,
        int index,
        SpreadsheetColumnFormat requested,
        IReadOnlyList<IReadOnlyList<string>> rows,
        bool isComputed)
    {
        var formatCode = SpreadsheetNumberFormatCode.Resolve(requested);
        var kind = ResolveKind(requested, index, rows, isComputed, ref formatCode);

        return new SpreadsheetLayoutColumn
        {
            Name = name,
            Index = index,
            Kind = kind,
            NumberFormatCode = formatCode,
            Width = requested?.Width ?? MeasureWidth(name, index, rows, isComputed),
            Style = requested?.Style is { IsEmpty: false } style ? style : null,
            Formula = string.IsNullOrWhiteSpace(requested?.Formula) ? null : requested.Formula,
            IsComputed = isComputed,
            Format = requested,
        };
    }

    private static SpreadsheetDataKind ResolveKind(
        SpreadsheetColumnFormat requested,
        int index,
        IReadOnlyList<IReadOnlyList<string>> rows,
        bool isComputed,
        ref string formatCode)
    {
        if (requested is not null && requested.DataKind != SpreadsheetDataKind.Auto)
        {
            return requested.DataKind;
        }

        if (SpreadsheetNumberFormatCode.IsNumeric(requested))
        {
            return SpreadsheetDataKind.Number;
        }

        if (SpreadsheetNumberFormatCode.IsDate(requested))
        {
            return SpreadsheetDataKind.Date;
        }

        // A computed column holds a formula whose result the spreadsheet types itself.
        if (isComputed)
        {
            return SpreadsheetDataKind.Number;
        }

        return InferKind(index, rows, ref formatCode);
    }

    private static SpreadsheetDataKind InferKind(
        int index,
        IReadOnlyList<IReadOnlyList<string>> rows,
        ref string formatCode)
    {
        var populated = 0;
        var numeric = 0;
        var dates = 0;
        var datesWithTime = 0;

        foreach (var row in rows)
        {
            if (row is null || index >= row.Count)
            {
                continue;
            }

            var value = row[index];

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            populated++;

            if (SpreadsheetValue.LooksNumeric(value, out _))
            {
                numeric++;

                continue;
            }

            if (SpreadsheetValue.LooksLikeDate(value, out _, out var hasTime))
            {
                dates++;

                if (hasTime)
                {
                    datesWithTime++;
                }
            }
        }

        if (populated == 0)
        {
            return SpreadsheetDataKind.Text;
        }

        // Only a column that is entirely numeric is stored numerically. One stray label in an otherwise
        // numeric column usually means the column is not a measure at all, and a mixed column would
        // break any aggregate written over it.
        if (numeric == populated)
        {
            return SpreadsheetDataKind.Number;
        }

        if (dates == populated)
        {
            // A date serial with no format renders as a bare number, which is worse than the text it
            // replaced, so an inferred date column always gets a format.
            formatCode ??= datesWithTime > 0
                ? "yyyy\\-mm\\-dd hh:mm"
                : "yyyy\\-mm\\-dd";

            return SpreadsheetDataKind.Date;
        }

        return SpreadsheetDataKind.Text;
    }

    private static double MeasureWidth(
        string name,
        int index,
        IReadOnlyList<IReadOnlyList<string>> rows,
        bool isComputed)
    {
        var longest = name?.Length ?? 0;

        if (!isComputed)
        {
            // Sampling bounds the cost on a very large export; the widest cell past this point is
            // rare enough that the reader can widen the column themselves.
            var sampled = 0;

            foreach (var row in rows)
            {
                if (row is not null && index < row.Count)
                {
                    longest = Math.Max(longest, row[index]?.Length ?? 0);
                }

                if (++sampled >= 200)
                {
                    break;
                }
            }
        }

        return Math.Clamp(longest + ColumnWidthPadding, MinimumColumnWidth, MaximumColumnWidth);
    }

    private static string NormalizeSheetName(string sheetName)
    {
        if (string.IsNullOrWhiteSpace(sheetName))
        {
            return "Sheet1";
        }

        var trimmed = sheetName.Trim();
        var builder = new System.Text.StringBuilder(trimmed.Length);

        foreach (var character in trimmed)
        {
            // These characters are reserved in worksheet names; a name containing one is rejected by
            // the spreadsheet application and the whole workbook fails to open.
            builder.Append(character is ':' or '\\' or '/' or '?' or '*' or '[' or ']'
                ? ' '
                : character);
        }

        var normalized = builder.ToString().Trim();

        if (normalized.Length == 0)
        {
            return "Sheet1";
        }

        return normalized.Length > 31
            ? normalized[..31]
            : normalized;
    }
}
