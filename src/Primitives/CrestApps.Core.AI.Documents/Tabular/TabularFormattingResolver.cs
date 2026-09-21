using CrestApps.Core.AI.Documents.Generation.Spreadsheets;

namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Resolves the formatting that describes how a table is presented.
/// </summary>
/// <remarks>
/// The export writes this formatting into the workbook and the preview draws a picture of it, and the two
/// have to agree: a preview showing a different header colour, or a raw number where the file shows a
/// currency amount, is a picture of a document the reader never receives. Both therefore resolve the
/// formatting here rather than each reading the recorded specification its own way.
/// </remarks>
internal static class TabularFormattingResolver
{
    /// <summary>
    /// Loads the recorded formatting and aligns its column references with the headers that were
    /// actually produced.
    /// <para>
    /// A full export writes the original source headers while a query writes SQL column names, so a
    /// specification recorded against one naming would silently format nothing against the other.
    /// Translating the names keeps a formatting request working no matter how the file is exported.
    /// </para>
    /// </summary>
    /// <param name="specJson">The stored specification.</param>
    /// <param name="header">The header row that was produced.</param>
    /// <param name="table">The table the formatting was recorded against.</param>
    /// <returns>The formatting to apply, or <see langword="null"/> when none is recorded.</returns>
    public static SpreadsheetFormatting Resolve(
        string specJson,
        List<string> header,
        TabularTableInfo table)
    {
        var formatting = SpreadsheetFormattingJson.Deserialize(specJson);

        if (table is null || header is null || header.Count == 0)
        {
            return formatting;
        }

        // The source file's own number formats are applied as defaults even when nothing was requested,
        // so a column that was currency in the upload comes back as currency.
        formatting = ApplySourceFormats(formatting, header, table);

        if (formatting is null)
        {
            return null;
        }

        var aliases = BuildColumnAliases(header, table);

        if (aliases.Count == 0)
        {
            return formatting;
        }

        foreach (var column in formatting.Columns)
        {
            column.Column = Translate(column.Column, aliases);
        }

        foreach (var conditional in formatting.ConditionalFormats)
        {
            conditional.Column = Translate(conditional.Column, aliases);
        }

        if (formatting.TotalRow?.Columns is not null)
        {
            foreach (var total in formatting.TotalRow.Columns)
            {
                total.Column = Translate(total.Column, aliases);
            }
        }

        foreach (var chart in formatting.Charts)
        {
            chart.CategoryColumn = Translate(chart.CategoryColumn, aliases);

            for (var index = 0; index < chart.ValueColumns.Count; index++)
            {
                chart.ValueColumns[index] = Translate(chart.ValueColumns[index], aliases);
            }
        }

        return formatting;
    }

    /// <summary>
    /// Seeds each column with the number format it had in the source file, for columns the caller did
    /// not format explicitly.
    /// <para>
    /// The formats are defaults, never overrides: a column the caller formatted keeps what they asked
    /// for. Only columns that were actually produced are seeded, so a query that aliases or aggregates
    /// a column does not inherit a format that no longer describes it.
    /// </para>
    /// </summary>
    /// <param name="formatting">The recorded formatting, which may be <see langword="null"/>.</param>
    /// <param name="header">The header row that was produced.</param>
    /// <param name="table">The source table.</param>
    /// <returns>The formatting including the inherited defaults, or <see langword="null"/> when there is nothing to apply.</returns>
    private static SpreadsheetFormatting ApplySourceFormats(
        SpreadsheetFormatting formatting,
        List<string> header,
        TabularTableInfo table)
    {
        var inherited = new List<SpreadsheetColumnFormat>();

        foreach (var name in header)
        {
            if (string.IsNullOrWhiteSpace(name) || formatting?.FindColumn(name) is not null)
            {
                continue;
            }

            var column = table.Columns.FirstOrDefault(candidate =>
                SpreadsheetFormatting.NameMatches(candidate.Name, name) ||
                SpreadsheetFormatting.NameMatches(candidate.SourceName, name));

            if (column is null || string.IsNullOrWhiteSpace(column.SourceFormat))
            {
                continue;
            }

            inherited.Add(new SpreadsheetColumnFormat
            {
                Column = name,
                FormatCode = column.SourceFormat,
            });
        }

        if (inherited.Count == 0)
        {
            return formatting;
        }

        formatting ??= new SpreadsheetFormatting();

        foreach (var column in inherited)
        {
            formatting.Columns.Add(column);
        }

        return formatting;
    }

    private static Dictionary<string, string> BuildColumnAliases(
        List<string> header,
        TabularTableInfo table)
    {
        var headerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in header)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                headerNames.Add(name.Trim());
            }
        }

        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in table.Columns)
        {
            if (string.IsNullOrWhiteSpace(column.SourceName) ||
                string.Equals(column.SourceName, column.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Only map toward a name that was actually written, so a reference that already matches is
            // never rewritten into one that does not.
            if (headerNames.Contains(column.SourceName) && !headerNames.Contains(column.Name))
            {
                aliases[column.Name] = column.SourceName;
            }
            else if (headerNames.Contains(column.Name) && !headerNames.Contains(column.SourceName))
            {
                aliases[column.SourceName] = column.Name;
            }
        }

        return aliases;
    }

    private static string Translate(string name, Dictionary<string, string> aliases)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return aliases.TryGetValue(name.Trim(), out var alias)
            ? alias
            : name;
    }
}
