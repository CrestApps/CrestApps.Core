using System.Text;

namespace CrestApps.Core.AI.Documents.Generation.Spreadsheets;

/// <summary>
/// Resolves <see cref="GeneratedFileContent"/> and its requested formatting into a concrete sheet plan:
/// the final column order, the storage type of every column, the number format codes, the column
/// widths, and the row numbers the header, data, and total row occupy.
/// <para>
/// The resolution lives here rather than in the writer so the decisions that matter most — above all,
/// whether a column is stored as a real number or as text — are plain to read and testable without
/// opening a generated file.
/// </para>
/// </summary>
public sealed class SpreadsheetLayout
{
    private const double MinimumColumnWidth = 9;
    private const double MaximumColumnWidth = 60;
    private const double ColumnWidthPadding = 2.5;

    // Used for a calculated column whose formula references nothing measurable.
    private const double FallbackComputedWidth = 16;

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
        var statistics = new List<ColumnStatistics>(header.Count);

        for (var index = 0; index < header.Count; index++)
        {
            var name = header[index] ?? string.Empty;
            var requested = formatting.FindColumn(name);
            var columnStatistics = ColumnStatistics.Measure(name, index, rows);

            statistics.Add(columnStatistics);
            columns.Add(Resolve(name, index, requested, columnStatistics, formatting, rows.Count, isComputed: false));
        }

        AppendComputedColumns(formatting, header, columns, statistics, rows.Count);

        return new SpreadsheetLayout(NormalizeSheetName(formatting.SheetName), columns, rows, formatting);
    }

    private static void AppendComputedColumns(
        SpreadsheetFormatting formatting,
        IReadOnlyList<string> header,
        List<SpreadsheetLayoutColumn> columns,
        List<ColumnStatistics> statistics,
        int rowCount)
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

            var name = requested.Column.Trim();

            // A calculated column has no values to measure, so its magnitude is taken from the columns
            // its formula reads. Without this the column is sized from its header alone and Excel shows
            // the result as ###### instead of a number.
            var columnStatistics = ColumnStatistics.ForFormula(name, columns.Count, requested.Formula, header, statistics);

            statistics.Add(columnStatistics);
            columns.Add(Resolve(name, columns.Count, requested, columnStatistics, formatting, rowCount, isComputed: true));
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
        ColumnStatistics statistics,
        SpreadsheetFormatting formatting,
        int rowCount,
        bool isComputed)
    {
        var formatCode = SpreadsheetNumberFormatCode.Resolve(requested);
        var kind = ResolveKind(requested, statistics, isComputed, ref formatCode);

        return new SpreadsheetLayoutColumn
        {
            Name = name,
            Index = index,
            Kind = kind,
            NumberFormatCode = formatCode,
            Width = requested?.Width ?? MeasureWidth(name, requested, kind, statistics, formatting, rowCount),
            Style = requested?.Style is { IsEmpty: false } style ? style : null,
            Formula = string.IsNullOrWhiteSpace(requested?.Formula) ? null : requested.Formula,
            IsComputed = isComputed,
            Format = requested,
        };
    }

    private static SpreadsheetDataKind ResolveKind(
        SpreadsheetColumnFormat requested,
        ColumnStatistics statistics,
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

        if (statistics.Populated == 0)
        {
            return SpreadsheetDataKind.Text;
        }

        // Only a column that is entirely numeric is stored numerically. One stray label in an otherwise
        // numeric column usually means the column is not a measure at all, and a mixed column would
        // break any aggregate written over it.
        if (statistics.Numeric == statistics.Populated)
        {
            return SpreadsheetDataKind.Number;
        }

        if (statistics.Dates == statistics.Populated)
        {
            // A date serial with no format renders as a bare number, which is worse than the text it
            // replaced, so an inferred date column always gets a format.
            formatCode ??= statistics.DatesWithTime > 0
                ? "yyyy\\-mm\\-dd hh:mm"
                : "yyyy\\-mm\\-dd";

            return SpreadsheetDataKind.Date;
        }

        return SpreadsheetDataKind.Text;
    }

    /// <summary>
    /// Works out how wide a column has to be for its values to be readable.
    /// <para>
    /// A numeric column is measured from what the reader will actually see, not from the raw value:
    /// a currency format adds a symbol, thousands separators, decimals, and a trailing alignment space,
    /// so <c>801005.25</c> occupies thirteen characters as <c>$801,005.25</c>. A column that is too
    /// narrow does not wrap — the spreadsheet application replaces the number with <c>######</c>, which
    /// looks like a broken file.
    /// </para>
    /// </summary>
    private static double MeasureWidth(
        string name,
        SpreadsheetColumnFormat requested,
        SpreadsheetDataKind kind,
        ColumnStatistics statistics,
        SpreadsheetFormatting formatting,
        int rowCount)
    {
        var width = (double)(name?.Length ?? 0);

        if (kind == SpreadsheetDataKind.Number)
        {
            var magnitude = statistics.MaxMagnitude;

            // A summed total is larger than any single row, and it sits in the same column, so the
            // column has to fit the total rather than the widest row.
            if (rowCount > 1 && IsSummed(formatting, name))
            {
                magnitude *= rowCount;
            }

            width = Math.Max(width, EstimateNumericWidth(requested, magnitude));
        }
        else if (kind == SpreadsheetDataKind.Date)
        {
            width = Math.Max(width, requested?.NumberFormat == SpreadsheetNumberFormat.DateTime ? 17 : 11);
        }
        else
        {
            width = Math.Max(width, statistics.MaxTextLength);
        }

        return Math.Clamp(width + ColumnWidthPadding, MinimumColumnWidth, MaximumColumnWidth);
    }

    private static bool IsSummed(SpreadsheetFormatting formatting, string name)
    {
        if (formatting.TotalRow?.Columns is null)
        {
            return false;
        }

        foreach (var total in formatting.TotalRow.Columns)
        {
            if (total is not null &&
                SpreadsheetFormatting.NameMatches(total.Column, name) &&
                total.Function is SpreadsheetAggregateFunction.Sum or SpreadsheetAggregateFunction.Count)
            {
                return true;
            }
        }

        return false;
    }

    private static double EstimateNumericWidth(SpreadsheetColumnFormat requested, double magnitude)
    {
        var numberFormat = requested?.NumberFormat ?? SpreadsheetNumberFormat.General;
        var value = Math.Abs(magnitude);

        if (numberFormat == SpreadsheetNumberFormat.Percent)
        {
            // A percentage renders its fraction multiplied by a hundred.
            value *= 100;
        }

        var integerDigits = value < 1
            ? 1
            : (int)Math.Floor(Math.Log10(value)) + 1;

        double width = integerDigits;

        var decimals = requested?.Decimals ?? numberFormat switch
        {
            SpreadsheetNumberFormat.Currency or SpreadsheetNumberFormat.Accounting => 2,
            _ => 0,
        };

        if (decimals > 0)
        {
            width += decimals + 1;
        }

        switch (numberFormat)
        {
            case SpreadsheetNumberFormat.Number:
            case SpreadsheetNumberFormat.Currency:
            case SpreadsheetNumberFormat.Accounting:
                width += Math.Max(0, (integerDigits - 1) / 3);

                break;

            case SpreadsheetNumberFormat.Percent:
                width += 1;

                break;
        }

        if (numberFormat is SpreadsheetNumberFormat.Currency or SpreadsheetNumberFormat.Accounting)
        {
            var symbol = string.IsNullOrWhiteSpace(requested?.CurrencySymbol)
                ? 1
                : requested.CurrencySymbol.Trim().Length;

            // The symbol, a possible minus sign, and the trailing space the bracketed negative format
            // reserves for alignment.
            width += symbol + 2;
        }
        else
        {
            width += 1;
        }

        return width;
    }

    private static string NormalizeSheetName(string sheetName)
    {
        if (string.IsNullOrWhiteSpace(sheetName))
        {
            return "Sheet1";
        }

        var trimmed = sheetName.Trim();
        var builder = new StringBuilder(trimmed.Length);

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

    /// <summary>
    /// What a single scan of a column's values tells us: how its type should be inferred, how wide its
    /// text is, and how large its numbers get.
    /// </summary>
    private sealed class ColumnStatistics
    {
        // Sampling bounds the cost on a very large export. The rows past this point are still written;
        // only the measurement is approximate.
        private const int SampleLimit = 500;

        public int Populated { get; private set; }

        public int Numeric { get; private set; }

        public int Dates { get; private set; }

        public int DatesWithTime { get; private set; }

        public int MaxTextLength { get; private set; }

        public double MaxMagnitude { get; private set; }

        public static ColumnStatistics Measure(string name, int index, IReadOnlyList<IReadOnlyList<string>> rows)
        {
            var statistics = new ColumnStatistics
            {
                MaxTextLength = name?.Length ?? 0,
            };

            var sampled = 0;

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

                statistics.Populated++;
                statistics.MaxTextLength = Math.Max(statistics.MaxTextLength, value.Length);

                if (SpreadsheetValue.LooksNumeric(value, out var number))
                {
                    statistics.Numeric++;
                    statistics.MaxMagnitude = Math.Max(statistics.MaxMagnitude, Math.Abs(number));
                }
                else if (SpreadsheetValue.LooksLikeDate(value, out _, out var hasTime))
                {
                    statistics.Dates++;

                    if (hasTime)
                    {
                        statistics.DatesWithTime++;
                    }
                }
                else if (SpreadsheetValue.TryParseNumber(value, out var decorated))
                {
                    // A value carrying a currency symbol is not plain enough to type the column on its
                    // own, but its magnitude still matters when the caller declares the column numeric.
                    statistics.MaxMagnitude = Math.Max(statistics.MaxMagnitude, Math.Abs(decorated));
                }

                if (++sampled >= SampleLimit)
                {
                    break;
                }
            }

            return statistics;
        }

        /// <summary>
        /// Derives the statistics of a calculated column from the columns its formula reads. A sum or
        /// difference of those columns cannot be wider than the widest of them plus a digit.
        /// </summary>
        public static ColumnStatistics ForFormula(
            string name,
            int index,
            string formula,
            IReadOnlyList<string> header,
            IReadOnlyList<ColumnStatistics> statistics)
        {
            var result = new ColumnStatistics
            {
                MaxTextLength = name?.Length ?? 0,
            };

            var matched = false;

            foreach (var reference in ReadReferences(formula))
            {
                for (var candidate = 0; candidate < header.Count && candidate < statistics.Count; candidate++)
                {
                    if (!SpreadsheetFormatting.NameMatches(header[candidate], reference))
                    {
                        continue;
                    }

                    matched = true;
                    result.MaxMagnitude = Math.Max(result.MaxMagnitude, statistics[candidate].MaxMagnitude);
                }
            }

            if (!matched)
            {
                // A formula that references nothing measurable (a literal, or a function over a range)
                // still needs a width that will not render as ######.
                result.MaxMagnitude = Math.Pow(10, FallbackComputedWidth / 2);
            }
            else
            {
                // A product of two referenced columns is wider than either, so a little headroom is
                // cheaper than a column that renders as ######.
                result.MaxMagnitude *= 10;
            }

            return result;
        }

        private static IEnumerable<string> ReadReferences(string formula)
        {
            if (string.IsNullOrEmpty(formula))
            {
                yield break;
            }

            var index = 0;

            while (index < formula.Length)
            {
                var open = formula.IndexOf('{', index);

                if (open < 0)
                {
                    yield break;
                }

                var close = formula.IndexOf('}', open + 1);

                if (close < 0)
                {
                    yield break;
                }

                var reference = formula[(open + 1)..close].Trim();

                if (!string.Equals(reference, "row", StringComparison.OrdinalIgnoreCase))
                {
                    yield return reference;
                }

                index = close + 1;
            }
        }
    }
}
