using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.CompilerServices;
using System.Text;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;

namespace CrestApps.Core.AI.Ingestion.Knowledge;

/// <summary>
/// The <c>Ingested</c> AI data source source handler. An ingested data source is a target bucket holding the
/// typed knowledge objects that ingesting files produced, and a read yields one row per object.
/// </summary>
/// <remarks>
/// Every row is already one chunk: the builder produced it inside the chunk budget and put the title on it,
/// so the indexing service stores it as it stands. Re-chunking would split a figure description away from the
/// figure it describes.
/// </remarks>
public sealed class FileAIDataSourceSourceHandler : IAIDataSourceSourceHandler
{
    /// <summary>
    /// How a chart's series are written into the indexed row. Camel case and dropped nulls keep the payload
    /// the retrieval side expects, and mean an unnamed series writes no name rather than a null one.
    /// </summary>
    private static readonly JsonSerializerOptions _seriesSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const int MaxTableRowsInContent = 200;
    private const int MaxChartPointsInContent = 200;

    private readonly IKnowledgeObjectStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileAIDataSourceSourceHandler"/> class.
    /// </summary>
    /// <param name="store">The knowledge object store.</param>
    public FileAIDataSourceSourceHandler(IKnowledgeObjectStore store)
    {
        _store = store;
    }

    /// <inheritdoc />
    public string SourceType => AIDataSourceSourceTypes.File;

    /// <inheritdoc />
    public bool ProducesTypedKnowledge => true;

    /// <inheritdoc />
    public ValueTask<string> GetReferenceTypeAsync(AIDataSource dataSource, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(SourceType);
    }

    /// <inheritdoc />
    public ValueTask ValidateAsync(AIDataSource dataSource, ValidationResultDetails result, CancellationToken cancellationToken = default)
    {
        // The data source is a bucket. What goes into it is decided by the uploads and indexers pointing at
        // it, so there is nothing on the record itself to validate.
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<KeyValuePair<string, SourceDocument>> ReadAsync(
        AIDataSource dataSource,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        var objects = await _store.GetByDataSourceIdAsync(dataSource.ItemId, cancellationToken);

        foreach (var entry in objects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pair = Map(entry);

            if (pair.HasValue)
            {
                yield return pair.Value;
            }
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<KeyValuePair<string, SourceDocument>> ReadByIdsAsync(
        AIDataSource dataSource,
        IEnumerable<string> documentIds,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(documentIds);

        var objects = await _store.GetByCanonicalIdsAsync(dataSource.ItemId, documentIds, cancellationToken);

        foreach (var entry in objects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pair = Map(entry);

            if (pair.HasValue)
            {
                yield return pair.Value;
            }
        }
    }

    /// <summary>
    /// Maps one knowledge object onto the row that gets embedded and stored.
    /// </summary>
    /// <param name="entry">The knowledge object.</param>
    /// <returns>The row, or <see langword="null"/> when the object has nothing to embed.</returns>
    private static KeyValuePair<string, SourceDocument>? Map(KnowledgeObject entry)
    {
        if (string.IsNullOrWhiteSpace(entry.CanonicalId))
        {
            return null;
        }

        // An excluded object is stored so the document stays complete and never indexed, so it can never be
        // returned as an answer.
        if (entry.Status == KnowledgeObjectStatus.Excluded)
        {
            return null;
        }

        // A chart nobody captioned still holds the points that were read off it, and a table nobody
        // introduced still holds its rows. Dropping those rows would lose what the reader did extract, so
        // the detail itself becomes the text. An object holding nothing is still dropped.
        var content = string.IsNullOrWhiteSpace(entry.Content)
            ? BuildContentFromDetails(entry)
            : entry.Content;

        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            [DataSourceConstants.ColumnNames.ContentType] = entry.ObjectType,
        };

        AddIfPresent(fields, DataSourceConstants.ColumnNames.RootId, entry.RootId);
        AddIfPresent(fields, DataSourceConstants.ColumnNames.ParentId, entry.ParentId);
        AddIfPresent(fields, "folio", entry.Folio);
        AddIfPresent(fields, "indexerId", entry.IndexerId);
        AddIfPresent(fields, "language", entry.Language);
        AddIfPresent(fields, "mediaType", entry.MediaType);

        if (entry.TryGet<PublicationDetails>(out var publication))
        {
            // What tells one issue from another issue of the same publication, and nothing else. The
            // publisher, the editor, the place and the ISSN are the same on every issue of a title, so
            // filtering on them narrows nothing the title does not already narrow, and each one costs a term
            // in the filter bag of every row of every document. They stay on the object, where a citation
            // that wants to spell the source out in full can still read them.
            AddIfPresent(fields, "publicationTitle", publication.PublicationTitle);
            AddIfPresent(fields, "volume", publication.Volume);
            AddIfPresent(fields, "issue", publication.Issue);
            AddIfPresent(fields, "issueDate", publication.Date);
        }

        if (entry.PageStart.HasValue)
        {
            fields[DataSourceConstants.ColumnNames.Page] = entry.PageStart.Value;
        }

        if (entry.TryGet<FigureDetails>(out var figure))
        {
            AddIfPresent(fields, "tier", figure.Tier);
            AddIfPresent(fields, "caption", figure.Caption);
        }

        if (entry.TryGet<TableDetails>(out var table))
        {
            AddIfPresent(fields, "caption", table.Caption);

            if (table.Columns is { Count: > 0 })
            {
                fields["columns"] = string.Join(", ", table.Columns);
            }
        }

        if (entry.TryGet<ChartDetails>(out var chart) && !string.IsNullOrWhiteSpace(chart.ValueConfidence))
        {
            fields["valueConfidence"] = chart.ValueConfidence;

            // Only a chart whose values were actually read off the page carries them into the index. An
            // estimate must not be stored where a later reader could mistake it for a measurement, which is
            // the whole reason the confidence levels exist. Stored uncapped: a retrieval that shows only the
            // first few points says how many there were, and a cap here would make that count a lie.
            if (string.Equals(chart.ValueConfidence, ChartValueConfidence.Exact, StringComparison.OrdinalIgnoreCase) &&
                chart.Series is { Count: > 0 })
            {
                fields["series"] = JsonSerializer.Serialize(chart.Series, _seriesSerializerOptions);
            }
        }

        var document = new SourceDocument
        {
            Title = entry.Title,
            Content = content,
            Fields = fields,
            IsPreChunked = true,
        };

        return new KeyValuePair<string, SourceDocument>(entry.CanonicalId, document);
    }

    /// <summary>
    /// Writes the text an object is found by out of the structured detail it carries, for an object nothing
    /// was written about.
    /// </summary>
    /// <param name="entry">The knowledge object.</param>
    /// <returns>The text, or <see langword="null"/> when the object carries no detail either.</returns>
    private static string BuildContentFromDetails(KnowledgeObject entry)
    {
        if (entry.TryGet<ChartDetails>(out var chart))
        {
            var content = BuildChartContent(chart);

            if (!string.IsNullOrWhiteSpace(content))
            {
                return content;
            }
        }

        if (entry.TryGet<TableDetails>(out var table))
        {
            return BuildTableContent(table);
        }

        return null;
    }

    /// <summary>
    /// Writes a chart out as what it is plotted against and the points that were read off it.
    /// </summary>
    /// <param name="chart">The chart detail.</param>
    /// <returns>The text, or <see langword="null"/> when no series carried anything.</returns>
    /// <remarks>
    /// Nothing is invented here. A series is only ever populated from the document's own geometry, so a row
    /// written from one says exactly what the document says.
    /// </remarks>
    private static string BuildChartContent(ChartDetails chart)
    {
        var series = new List<string>();

        foreach (var entry in chart.Series ?? [])
        {
            var line = FormatSeries(entry);

            if (!string.IsNullOrWhiteSpace(line))
            {
                series.Add(line);
            }
        }

        if (series.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();

        Append(builder, chart.ChartType);
        Append(builder, Join(" | ", [chart.AxisX, chart.AxisY]));

        foreach (var line in series)
        {
            Append(builder, line);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Writes one series out as its name and its points.
    /// </summary>
    /// <param name="series">The series.</param>
    /// <returns>The text, or <see langword="null"/> when the series is unnamed and carries no point.</returns>
    private static string FormatSeries(ChartSeries series)
    {
        if (series is null)
        {
            return null;
        }

        var points = new List<string>();

        foreach (var point in series.Points ?? [])
        {
            if (point is not { Length: > 0 })
            {
                continue;
            }

            points.Add($"({Join(", ", point.Select(value => value.ToString(CultureInfo.InvariantCulture)))})");

            if (points.Count == MaxChartPointsInContent)
            {
                break;
            }
        }

        if (points.Count == 0)
        {
            return series.Name;
        }

        var plotted = Join("; ", points);

        return string.IsNullOrWhiteSpace(series.Name) ? plotted : $"{series.Name}: {plotted}";
    }

    /// <summary>
    /// Writes a table out as its column headers and the rows under them.
    /// </summary>
    /// <param name="table">The table detail.</param>
    /// <returns>The text, or <see langword="null"/> when the table has no row with anything in it.</returns>
    /// <remarks>
    /// Each row carries its column names, so a row retrieved on its own still says what its values mean.
    /// </remarks>
    private static string BuildTableContent(TableDetails table)
    {
        var columns = table.Columns ?? [];
        var rows = new List<string>();

        foreach (var row in table.Rows ?? [])
        {
            if (rows.Count == MaxTableRowsInContent)
            {
                break;
            }

            if (row is null)
            {
                continue;
            }

            var cells = new List<string>();

            for (var column = 0; column < row.Length; column++)
            {
                if (string.IsNullOrWhiteSpace(row[column]))
                {
                    continue;
                }

                var name = column < columns.Count ? columns[column] : null;

                cells.Add(string.IsNullOrWhiteSpace(name) ? row[column] : $"{name}={row[column]}");
            }

            if (cells.Count == 0)
            {
                continue;
            }

            rows.Add(Join("; ", cells));
        }

        if (rows.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();

        Append(builder, Join(" | ", columns));

        foreach (var line in rows)
        {
            Append(builder, line);
        }

        return builder.ToString();
    }

    private static void AddIfPresent(Dictionary<string, object> fields, string name, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields[name] = value;
        }
    }

    private static void Append(StringBuilder builder, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.Append('\n');
        }

        builder.Append(value);
    }

    private static string Join(string separator, IEnumerable<string> parts)
    {
        return string.Join(separator, parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }
}
