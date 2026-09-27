using System.Globalization;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Documents.Presentations.Editing;
using CrestApps.Core.AI.Documents.Tabular;
using Microsoft.Data.Sqlite;

namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Fetches what element requests refer to — uploaded pictures, generated pictures and rows of tabular data —
/// so the engine is handed complete specifications.
/// </summary>
internal static class PresentationContentResolver
{
    /// <summary>
    /// Resolves the content of element requests, including those inside groups.
    /// </summary>
    /// <param name="session">The tool session.</param>
    /// <param name="requests">The requests.</param>
    /// <param name="notes">Where to record what was fetched.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when every request is resolved.</returns>
    public static async Task ResolveAsync(PresentationToolSession session, IEnumerable<PresentationElementRequest> requests, List<string> notes, CancellationToken cancellationToken)
    {
        foreach (var request in requests)
        {
            await ResolveAsync(session, request, notes, cancellationToken);

            if (request.Children.Count > 0)
            {
                await ResolveAsync(session, request.Children, notes, cancellationToken);
            }
        }
    }

    private static async Task ResolveAsync(PresentationToolSession session, PresentationElementRequest request, List<string> notes, CancellationToken cancellationToken)
    {
        var spec = request.Spec;

        if (spec.Kind == PresentationElementSpecKind.Image && spec.Image is null)
        {
            if (!string.IsNullOrWhiteSpace(request.ImageDocument))
            {
                spec.Image = await session.LoadImageAsync(request.ImageDocument);
                spec.AltText ??= Path.GetFileNameWithoutExtension(spec.Image.FileName);
            }
            else if (!string.IsNullOrWhiteSpace(request.ImagePrompt))
            {
                var shape = spec.Bounds is { Width: not null, Height: not null } bounds && bounds.Height.Value.Value > bounds.Width.Value.Value ? "portrait" : "landscape";
                var (image, error) = await PresentationImageGenerator.GenerateAsync(session.Services, request.ImagePrompt, shape, cancellationToken);

                spec.Image = image ?? throw new PresentationArgumentException(error);
                spec.AltText ??= request.ImagePrompt.Length > 150 ? request.ImagePrompt[..147] + "..." : request.ImagePrompt;
                notes.Add("Generated a picture of: " + request.ImagePrompt);
            }
            else
            {
                throw new PresentationArgumentException("An image element needs image_document_id (an uploaded picture) or image_prompt (a picture to generate).");
            }
        }

        if (string.IsNullOrWhiteSpace(request.TabularSql))
        {
            return;
        }

        if (spec.Kind is not (PresentationElementSpecKind.Table or PresentationElementSpecKind.Chart))
        {
            throw new PresentationArgumentException("tabular_sql fills a table or a chart; set the element's type to table or chart.");
        }

        var result = await QueryAsync(session.Services, request.TabularSql, request.MaxRows, cancellationToken);

        if (spec.Kind == PresentationElementSpecKind.Table)
        {
            var existing = spec.Table ?? new PresentationTableSpec();
            spec.Table = BuildTable(result, existing);
            notes.Add($"Filled the table from the query ({result.Rows.Count.ToString(CultureInfo.InvariantCulture)} row(s){(result.Truncated ? ", truncated to " + request.MaxRows.ToString(CultureInfo.InvariantCulture) : string.Empty)}).");
        }
        else
        {
            var chart = spec.Chart ?? new PresentationChartSpec();
            FillChart(chart, result, request.CategoryColumn, request.ValueColumns);
            spec.Chart = chart;
            notes.Add($"Filled the chart from the query ({result.Rows.Count.ToString(CultureInfo.InvariantCulture)} categor{(result.Rows.Count == 1 ? "y" : "ies")}, {chart.Series.Count.ToString(CultureInfo.InvariantCulture)} series).");
        }
    }

    /// <summary>
    /// Runs a read-only query over the conversation's tabular data.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <param name="sql">The query.</param>
    /// <param name="maxRows">The most rows to return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result.</returns>
    /// <exception cref="PresentationArgumentException">The query could not be run.</exception>
    public static async Task<TabularQueryResult> QueryAsync(IServiceProvider services, string sql, int maxRows, CancellationToken cancellationToken)
    {
        var preparation = await TabularToolRunner.PrepareAsync(services, cancellationToken);

        if (preparation.Error is not null)
        {
            throw new PresentationArgumentException("The table or chart could not be filled from tabular data: " + preparation.Error);
        }

        using var workspace = preparation.Workspace;

        try
        {
            return await workspace.QueryAsync(sql, Math.Clamp(maxRows, 1, 200), cancellationToken);
        }
        catch (TabularSqlException exception)
        {
            throw new PresentationArgumentException("The query failed: " + exception.Message + " Use list_tabular_data to see the table and column names.");
        }
        catch (SqliteException exception)
        {
            throw new PresentationArgumentException("The query failed: " + exception.Message + " Use list_tabular_data to see the table and column names.");
        }
    }

    /// <summary>
    /// Turns a query result into table rows, header first.
    /// </summary>
    /// <param name="result">The query result.</param>
    /// <param name="existing">The table request whose style and widths are kept.</param>
    /// <returns>The table.</returns>
    public static PresentationTableSpec BuildTable(TabularQueryResult result, PresentationTableSpec existing)
    {
        var table = new PresentationTableSpec
        {
            HeaderRow = true,
            ColumnWidths = existing?.ColumnWidths ?? [],
            ColumnAlignments = existing?.ColumnAlignments ?? [],
            Style = existing?.Style,
        };

        table.Rows.Add([.. result.Columns]);

        foreach (var row in result.Rows)
        {
            table.Rows.Add(row.Select(FormatCell).ToList());
        }

        return table;
    }

    /// <summary>
    /// Fills a chart's categories and series from a query result.
    /// </summary>
    /// <param name="chart">The chart.</param>
    /// <param name="result">The query result.</param>
    /// <param name="categoryColumn">The column the categories come from; the first when not given.</param>
    /// <param name="valueColumns">The columns plotted; every numeric column when none are given.</param>
    public static void FillChart(PresentationChartSpec chart, TabularQueryResult result, string categoryColumn, IList<string> valueColumns)
    {
        if (result.Columns.Count < 2 && valueColumns is not { Count: > 0 })
        {
            throw new PresentationArgumentException("A chart query needs at least two columns: one for the categories and one or more of numbers.");
        }

        var categoryIndex = IndexOf(result, categoryColumn) ?? 0;
        var indexes = new List<int>();

        if (valueColumns is { Count: > 0 })
        {
            foreach (var name in valueColumns)
            {
                indexes.Add(IndexOf(result, name) ?? throw new PresentationArgumentException($"The query has no column \"{name}\". Its columns are: {string.Join(", ", result.Columns)}."));
            }
        }
        else
        {
            for (var column = 0; column < result.Columns.Count; column++)
            {
                if (column != categoryIndex && result.Rows.Count > 0 && result.Rows.Count(row => ToNumber(row[column]) is not null) * 2 >= result.Rows.Count)
                {
                    indexes.Add(column);
                }
            }
        }

        if (indexes.Count == 0)
        {
            throw new PresentationArgumentException($"The query has no numeric column to plot. Its columns are: {string.Join(", ", result.Columns)}.");
        }

        chart.Categories = result.Rows.Select(row => FormatCell(row[categoryIndex])).ToList();
        chart.Series = indexes.Select(index => new PresentationChartSeriesSpec
        {
            Name = result.Columns[index],
            Values = result.Rows.Select(row => ToNumber(row[index])).ToList(),
        }).ToList();
    }

    private static int? IndexOf(TabularQueryResult result, string column)
    {
        if (string.IsNullOrWhiteSpace(column))
        {
            return null;
        }

        for (var index = 0; index < result.Columns.Count; index++)
        {
            if (string.Equals(result.Columns[index], column.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        throw new PresentationArgumentException($"The query has no column \"{column}\". Its columns are: {string.Join(", ", result.Columns)}.");
    }

    private static double? ToNumber(object value)
    {
        return value switch
        {
            null => null,
            double number => number,
            float number => number,
            decimal number => (double)number,
            long number => number,
            int number => number,
            string text when double.TryParse(text.Replace(",", string.Empty, StringComparison.Ordinal).Trim('$', '%', ' '), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    private static string FormatCell(object value)
    {
        return value switch
        {
            null => string.Empty,
            double number => FormatNumber(number),
            float number => FormatNumber(number),
            decimal number => FormatNumber((double)number),
            long number => number.ToString("#,##0", CultureInfo.InvariantCulture),
            int number => number.ToString("#,##0", CultureInfo.InvariantCulture),
            DateTime date => date.TimeOfDay == TimeSpan.Zero ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    private static string FormatNumber(double number)
    {
        return Math.Abs(number - Math.Round(number)) < 1e-9
            ? number.ToString("#,##0", CultureInfo.InvariantCulture)
            : number.ToString("#,##0.##", CultureInfo.InvariantCulture);
    }
}
