using System.Globalization;
using CrestApps.Core.AI.Documents.Generation.Spreadsheets;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using CrestApps.Core.AI.Documents.Tabular;
using Microsoft.Data.Sqlite;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Readies blocks that arrive in a tool call for the document: gives each an identifier and reads the data
/// the model pointed at — a <c>[chart:…]</c> marker, a query over the uploaded spreadsheets — into the block.
/// </summary>
internal static class PdfBlockPreparer
{
    private const int DefaultMaxRows = 1000;
    private const int MaxRowsLimit = 5000;

    /// <summary>
    /// Readies blocks for a document.
    /// </summary>
    /// <param name="definition">The document the blocks are added to, which issues their identifiers.</param>
    /// <param name="blocks">The blocks.</param>
    /// <param name="services">The request services, used to reach the tabular workspace.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Notes about data that could not be read as asked.</returns>
    public static async Task<List<string>> PrepareAsync(
        PdfDocumentDefinition definition,
        List<PdfBlockDefinition> blocks,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var warnings = new List<string>();

        if (blocks is null || blocks.Count == 0)
        {
            return warnings;
        }

        ReplaceHandWrittenContents(definition, blocks, warnings);

        TabularToolRunner.PreparationResult? tabular = null;

        try
        {
            foreach (var block in blocks)
            {
                if (block is null)
                {
                    continue;
                }

                block.Id = "b" + definition.NextBlockNumber++.ToString(CultureInfo.InvariantCulture);

                var type = PdfMigraDocBuilder.NormalizeType(block);

                if (!string.IsNullOrEmpty(type))
                {
                    block.Type = type;
                }

                if (block.Chart is { } chart && !string.IsNullOrWhiteSpace(chart.ChartJs))
                {
                    ReadChartJs(chart, warnings);
                }

                var needsTabular = block.Table?.Source is not null || block.Chart?.Source is not null;

                if (!needsTabular)
                {
                    continue;
                }

                tabular ??= await TabularToolRunner.PrepareAsync(services, cancellationToken);

                if (tabular.Value.Error is not null)
                {
                    throw new PdfToolException($"Block {block.Id} reads uploaded tabular data, but none can be read: {tabular.Value.Error}");
                }

                if (block.Table?.Source is { } tableSource)
                {
                    await ReadTableAsync(block.Table, tableSource, tabular.Value, cancellationToken);
                    block.Table.Source = null;
                }

                if (block.Chart?.Source is { } chartSource)
                {
                    await ReadChartAsync(block.Chart, chartSource, tabular.Value, cancellationToken);
                    block.Chart.Source = null;
                }
            }
        }
        finally
        {
            tabular?.Workspace?.Dispose();
        }

        return warnings;
    }

    /// <summary>
    /// Replaces a table of contents written as content — a "Table of Contents" heading over a list of the
    /// document's headings — with the generated one, which has page numbers and links and stays current.
    /// </summary>
    /// <param name="definition">The document.</param>
    /// <param name="blocks">The blocks of the call, changed in place.</param>
    /// <param name="notes">Receives a note when one was replaced.</param>
    internal static void ReplaceHandWrittenContents(PdfDocumentDefinition definition, List<PdfBlockDefinition> blocks, List<string> notes)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(blocks);

        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];

            if (block is null || PdfMigraDocBuilder.NormalizeType(block) != PdfBlockTypes.Heading || !IsContentsTitle(block.Text))
            {
                continue;
            }

            var end = index + 1;
            var next = end < blocks.Count ? blocks[end] : null;

            // The list under it is the hand-written entries; it is taken only when its items are the
            // document's own headings, so an ordinary list under a heading called "Contents" is kept.
            if (next is not null && PdfMigraDocBuilder.NormalizeType(next) == PdfBlockTypes.List)
            {
                if (!ListsHeadings(next, definition, blocks))
                {
                    continue;
                }

                end++;
            }

            // The generated contents get a page of their own, so a break the model put after its own is not
            // needed and would leave a blank page.
            if (end < blocks.Count && blocks[end] is { } after && PdfMigraDocBuilder.NormalizeType(after) == PdfBlockTypes.PageBreak)
            {
                end++;
            }

            blocks.RemoveRange(index, end - index);

            definition.TableOfContents ??= new PdfTableOfContentsDefinition();
            definition.TableOfContents.Enabled = true;
            definition.TableOfContents.Title ??= Plain(block.Text);

            notes.Add("The table of contents written as a heading and a list was replaced with a generated one (table_of_contents), which shows each heading's page number and links to it. Do not write one by hand.");

            return;
        }
    }

    private static bool IsContentsTitle(string text)
    {
        return Plain(text).TrimEnd(':').ToLowerInvariant() is "table of contents" or "contents" or "toc" or "content";
    }

    private static bool ListsHeadings(PdfBlockDefinition list, PdfDocumentDefinition definition, List<PdfBlockDefinition> blocks)
    {
        var items = (list.Items ?? []).Select(EntryText).Where(item => item.Length > 0).ToList();

        if (items.Count == 0)
        {
            return false;
        }

        var headings = new HashSet<string>(
            (definition.Sections ?? []).SelectMany(section => section.Blocks ?? []).Concat(blocks)
                .Where(candidate => candidate is not null && PdfMigraDocBuilder.NormalizeType(candidate) == PdfBlockTypes.Heading && !IsContentsTitle(candidate.Text))
                .Select(candidate => EntryText(candidate.Text)),
            StringComparer.OrdinalIgnoreCase);

        return items.Count(headings.Contains) * 2 >= items.Count;
    }

    private static string EntryText(string text)
    {
        // "1. Revenue Analysis ........ 3" names the heading "Revenue Analysis".
        var plain = Plain(text);
        var start = 0;

        while (start < plain.Length && (char.IsDigit(plain[start]) || plain[start] is '.' or ')' or '-' or '•' or '*' or ' '))
        {
            start++;
        }

        var end = plain.Length;

        while (end > start && (char.IsDigit(plain[end - 1]) || plain[end - 1] is '.' or ' ' or '…'))
        {
            end--;
        }

        return plain[start..end].Trim();
    }

    private static string Plain(string text)
    {
        return (text ?? string.Empty).Replace("**", string.Empty, StringComparison.Ordinal).Replace("__", string.Empty, StringComparison.Ordinal).Trim();
    }

    private static void ReadChartJs(PdfChartDefinition chart, List<string> warnings)
    {
        if (!PdfChartJsReader.TryRead(chart.ChartJs, out var read))
        {
            warnings.Add("The chart_js value is not a Chart.js configuration with data; pass labels and series instead.");
            chart.ChartJs = null;

            return;
        }

        chart.ChartType ??= read.ChartType;
        chart.Title ??= read.Title;

        if (chart.Labels is not { Count: > 0 })
        {
            chart.Labels = read.Labels;
        }

        if (chart.Series is not { Count: > 0 })
        {
            chart.Series = read.Series;
        }

        chart.SourceDescription ??= "Chart from the conversation";
        chart.ChartJs = null;
    }

    private static async Task ReadTableAsync(
        PdfTableDefinition table,
        PdfTabularSourceDefinition source,
        TabularToolRunner.PreparationResult tabular,
        CancellationToken cancellationToken)
    {
        var (result, headers, formatting, total, description) = await QueryAsync(source, tabular, cancellationToken);

        if (table.Columns is not { Count: > 0 })
        {
            table.Columns = [];

            foreach (var header in headers)
            {
                table.Columns.Add(new PdfTableColumnDefinition { Header = header });
            }
        }

        // A column the model did not format takes the format the spreadsheet export would use, so the table
        // reads the same as the workbook and its preview.
        for (var index = 0; index < table.Columns.Count && index < headers.Count; index++)
        {
            var column = table.Columns[index];

            column.Header ??= headers[index];

            if (string.IsNullOrWhiteSpace(column.Format) && string.IsNullOrWhiteSpace(column.FormatCode) && formatting is not null)
            {
                var code = SpreadsheetNumberFormatCode.Resolve(formatting.FindColumn(headers[index]));

                if (!string.IsNullOrWhiteSpace(code) &&
                    !string.Equals(code, "General", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(code, "@", StringComparison.Ordinal))
                {
                    column.FormatCode = code;
                }
            }
        }

        table.Rows = [.. result.Rows.Select(row => row.Select(ToCellText).ToList())];
        table.SourceDescription = description;
        table.SourceRowCount = total > result.Rows.Count ? total : null;
    }

    private static async Task ReadChartAsync(
        PdfChartDefinition chart,
        PdfTabularSourceDefinition source,
        TabularToolRunner.PreparationResult tabular,
        CancellationToken cancellationToken)
    {
        var (result, headers, _, _, description) = await QueryAsync(source, tabular, cancellationToken);

        if (headers.Count == 0 || result.Rows.Count == 0)
        {
            throw new PdfToolException("The chart's data query returned no rows.");
        }

        var labelIndex = string.IsNullOrWhiteSpace(source.LabelColumn)
            ? 0
            : FindColumn(headers, source.LabelColumn);

        if (labelIndex < 0)
        {
            throw new PdfToolException($"The chart names the label column \"{source.LabelColumn}\", which the query does not return. It returns: {string.Join(", ", headers)}.");
        }

        var valueIndexes = new List<int>();

        if (source.ValueColumns is { Count: > 0 })
        {
            foreach (var name in source.ValueColumns)
            {
                var index = FindColumn(headers, name);

                if (index < 0)
                {
                    throw new PdfToolException($"The chart names the value column \"{name}\", which the query does not return. It returns: {string.Join(", ", headers)}.");
                }

                valueIndexes.Add(index);
            }
        }
        else
        {
            for (var index = 0; index < headers.Count; index++)
            {
                if (index != labelIndex && result.Rows.All(row => row[index] is null or DBNull or long or int or double or float or decimal))
                {
                    valueIndexes.Add(index);
                }
            }
        }

        if (valueIndexes.Count == 0)
        {
            throw new PdfToolException("The chart's query returns no numeric column to plot; name one in source.value_columns.");
        }

        chart.Labels = [.. result.Rows.Select(row => ToCellText(row[labelIndex]))];
        chart.Series = [];

        foreach (var index in valueIndexes)
        {
            chart.Series.Add(new PdfChartSeriesDefinition
            {
                Name = headers[index],
                Values = [.. result.Rows.Select(row => ToNumber(row[index]))],
            });
        }

        chart.SourceDescription = description;
    }

    private static async Task<(TabularQueryResult Result, List<string> Headers, SpreadsheetFormatting Formatting, long Total, string Description)> QueryAsync(
        PdfTabularSourceDefinition source,
        TabularToolRunner.PreparationResult tabular,
        CancellationToken cancellationToken)
    {
        var maxRows = Math.Clamp(source.MaxRows ?? DefaultMaxRows, 1, MaxRowsLimit);
        TabularTableInfo table = null;
        string sql;

        if (!string.IsNullOrWhiteSpace(source.Sql))
        {
            sql = source.Sql.Trim().TrimEnd(';');
        }
        else if (!string.IsNullOrWhiteSpace(source.TableName))
        {
            var name = source.TableName.Trim();

            table = tabular.Tables.FirstOrDefault(candidate => string.Equals(candidate.TableName, name, StringComparison.OrdinalIgnoreCase))
                ?? tabular.Tables.FirstOrDefault(candidate => string.Equals(candidate.WorksheetName, name, StringComparison.OrdinalIgnoreCase))
                ?? tabular.Tables.FirstOrDefault(candidate => string.Equals(candidate.SourceFileName, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new PdfToolException($"There is no loaded table named \"{name}\". Loaded tables: {string.Join(", ", tabular.Tables.Select(candidate => candidate.TableName))}.");

            sql = "SELECT * FROM " + TabularWorkspaceSqliteHelpers.QuoteIdentifier(table.TableName);
        }
        else
        {
            throw new PdfToolException("A tabular source needs 'sql' or 'table_name'.");
        }

        TabularQueryResult result;

        try
        {
            result = await tabular.Workspace.QueryAsync(sql, maxRows, cancellationToken);
        }
        catch (Exception ex) when (ex is TabularSqlException or SqliteException)
        {
            throw new PdfToolException($"The tabular query failed: {ex.Message}", ex);
        }

        var headers = new List<string>(result.Columns.Count);

        foreach (var column in result.Columns)
        {
            var info = table?.Columns.FirstOrDefault(candidate => string.Equals(candidate.Name, column, StringComparison.OrdinalIgnoreCase));

            // A full table is headed the way the uploaded file headed it, not by its SQL identifiers.
            headers.Add(string.IsNullOrWhiteSpace(info?.SourceName) ? column : info.SourceName);
        }

        var stored = table is null
            ? await tabular.Workspace.GetFormattingAsync(TabularToolNames.WorkspaceFormattingKey, cancellationToken)
            : await tabular.Workspace.GetFormattingAsync(table.TableName, cancellationToken);

        if (table is not null && string.IsNullOrEmpty(stored.SpecJson))
        {
            stored = await tabular.Workspace.GetFormattingAsync(TabularToolNames.WorkspaceFormattingKey, cancellationToken);
        }

        var formatting = TabularFormattingResolver.Resolve(stored.SpecJson, headers, table);
        long total = result.Rows.Count;

        if (result.Truncated)
        {
            try
            {
                var counted = await tabular.Workspace.QueryAsync($"SELECT COUNT(*) FROM ({sql})", 1, cancellationToken);

                if (counted.Rows.Count > 0 && counted.Rows[0].Length > 0 &&
                    long.TryParse(Convert.ToString(counted.Rows[0][0], CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var counts))
                {
                    total = counts;
                }
            }
            catch (Exception ex) when (ex is TabularSqlException or SqliteException)
            {
                // The rows on hand are all that can be reported.
            }
        }

        var description = table is null
            ? "Query over the uploaded tabular data"
            : string.IsNullOrWhiteSpace(table.WorksheetName)
                ? $"Table {table.TableName} from {table.SourceFileName}"
                : $"Worksheet \"{table.WorksheetName}\" of {table.SourceFileName}";

        return (result, headers, formatting, total, description);
    }

    private static int FindColumn(List<string> headers, string name)
    {
        return headers.FindIndex(header => SpreadsheetFormatting.NameMatches(header, name));
    }

    private static string ToCellText(object value)
    {
        return value switch
        {
            null or DBNull => string.Empty,
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            float number => number.ToString("R", CultureInfo.InvariantCulture),
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            DateTime date => date.TimeOfDay == TimeSpan.Zero
                ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    private static double? ToNumber(object value)
    {
        return value switch
        {
            null or DBNull => null,
            double number => number,
            float number => number,
            decimal number => (double)number,
            long number => number,
            int number => number,
            string text when PdfCellValues.TryParseNumber(text, percentAsFraction: false, out var parsed) => parsed,
            _ => null,
        };
    }
}
