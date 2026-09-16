using System.Text.Json;
using System.Text.Json.Serialization;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Infrastructure;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using Cysharp.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Collects the figures and tables among the hits, and renders the blocks that name them.
/// </summary>
/// <remarks>
/// A figure is not readable as prose. Naming it with a label and a page is what lets a model cite it, and
/// registering that label against the host's own address is what lets the picture be shown without the
/// address ever passing through the model.
/// </remarks>
internal sealed class TypedResultCollector
{
    // A figures block is a list of figures a model reads to decide what to ask about next, not a data dump.
    // A line traced off a polyline can carry hundreds of points, and a handful of such charts among the hits
    // would bury the labels and the instruction the block exists to give. Twenty-four points holds a chart
    // plotted month by month across two years whole, which is most of the charts anyone plots, and four
    // series holds more than a legend a reader keeps in their head. Anything past that is left out and said
    // to be left out - never quietly shortened, because a model shown part of a chart as though it were the
    // whole one answers about the part with the confidence of the whole.
    private const int MaxInlinedPointsPerSeries = 24;
    private const int MaxInlinedSeries = 4;

    // Reads what the row stored and writes what is kept: a series nobody named stays unnamed rather than
    // being written out as a null, and the legend parsing that would give it a name is not this.
    private static readonly JsonSerializerOptions _seriesJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IServiceProvider _services;
    private readonly string _dataSourceId;
    private readonly string _sourceType;
    private readonly AIInvocationContext _invocationContext;
    private readonly ILogger _logger;
    private readonly List<RetrievedFigure> _figures = [];

    // The reference type and data source a figure's row came from are not carried on the figure itself,
    // and the reference registered for it needs both to resolve the way a citation does. Appended in
    // lockstep with the figures, so entry i belongs to _figures[i].
    private readonly List<(string ReferenceType, string DataSourceId)> _figureOrigins = [];

    // The points a chart was read at are not carried on the figure itself. Appended in lockstep with the
    // figures, so entry i belongs to _figures[i], and null for every figure that is not a chart whose
    // values came from the document.
    private readonly List<List<ChartSeries>> _figureSeries = [];
    private readonly List<RetrievedTable> _tables = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="TypedResultCollector"/> class.
    /// </summary>
    /// <param name="services">The request services, used to resolve the host's figure links.</param>
    /// <param name="dataSourceId">The data source the hits came from.</param>
    /// <param name="sourceType">The kind of data source the hits came from, used when a row omits its own.</param>
    /// <param name="invocationContext">The active invocation context, or <see langword="null"/> when the tool runs outside one.</param>
    /// <param name="logger">The logger, so a link that cannot be built says why.</param>
    public TypedResultCollector(
        IServiceProvider services,
        string dataSourceId,
        string sourceType,
        AIInvocationContext invocationContext,
        ILogger logger)
    {
        _services = services;
        _dataSourceId = dataSourceId;
        _sourceType = sourceType;
        _invocationContext = invocationContext;
        _logger = logger;
    }

    /// <summary>
    /// Gets the figures and charts among the hits.
    /// </summary>
    public IReadOnlyList<RetrievedFigure> Figures => _figures;

    /// <summary>
    /// Gets the tables among the hits.
    /// </summary>
    public IReadOnlyList<RetrievedTable> Tables => _tables;

    /// <summary>
    /// Records the figures and tables among the supplied hits.
    /// </summary>
    /// <param name="results">The selected search results.</param>
    public void Collect(IReadOnlyList<DataSourceSearchResult> results)
    {
        if (_logger?.IsEnabled(LogLevel.Debug) == true)
        {
            _logger.LogDebug(
                "Retrieval returned {Count} hit(s) with content types: {ContentTypes}.",
                results.Count,
                string.Join(", ", results.Select(result => result.ContentType ?? "(null)").Distinct()));
        }

        foreach (var result in results)
        {
            switch (ResolveContentType(result))
            {
                case KnowledgeObjectTypes.Figure:
                case KnowledgeObjectTypes.Chart:
                    var valueConfidence = ReadFilter(result, "valueConfidence");

                    _figures.Add(new RetrievedFigure
                    {
                        Id = result.ReferenceId,
                        Label = $"[fig:{_figures.Count + 1}]",
                        Title = result.Title,
                        Caption = ReadFilter(result, "caption"),
                        Uri = $"crestapps://datasource/{_dataSourceId}/figure/{result.ReferenceId}",
                        Link = ResolveLink(result),
                        MediaType = ReadFilter(result, "mediaType"),
                        ValueConfidence = valueConfidence,
                        ContentType = result.ContentType,
                        Page = result.Page,
                    });

                    // The searched data source answers for a row that names none, because the route that
                    // serves a figure is scoped to a data source and an unscoped identifier opens nothing.
                    _figureOrigins.Add((ResolveReferenceType(result), result.DataSourceId ?? _dataSourceId));

                    // This is the gate the whole confidence model exists for, and it is read here so it
                    // cannot be forgotten at the point the numbers are printed. Only a chart whose values
                    // were lifted from the document itself has points worth carrying; at any other level
                    // they were estimated off a picture, and an estimate printed as numbers is
                    // indistinguishable from a measurement by the time a model quotes it.
                    _figureSeries.Add(
                        string.Equals(valueConfidence, ChartValueConfidence.Exact, StringComparison.OrdinalIgnoreCase)
                            ? ParseSeries(ReadFilter(result, "series"), result.ReferenceId)
                            : null);

                    break;

                case KnowledgeObjectTypes.Table:
                    _tables.Add(new RetrievedTable
                    {
                        Id = result.ReferenceId,
                        Label = $"[tbl:{_tables.Count + 1}]",
                        Title = result.Title,
                        Caption = ReadFilter(result, "caption"),
                        Columns = ReadFilter(result, "columns"),
                        Page = result.Page,
                    });

                    break;
            }
        }
    }

    /// <summary>
    /// Works out what kind of knowledge a hit holds.
    /// </summary>
    /// <param name="result">The hit.</param>
    /// <returns>The content type to treat it as.</returns>
    /// <remarks>
    /// The typed column is the answer when the index returns one, but it is not the only evidence, and
    /// relying on it alone made a whole class of index silently lose its pictures: one built before the
    /// column existed, one whose provider drops fields it has no mapping for, or one whose schema
    /// upgrade could not run. Every knowledge row's identifier already states its kind — a figure's
    /// begins <c>figure:</c> — and that travels with the row wherever it is stored, so it stands in when
    /// the column says nothing. A figure read as text keeps its caption and loses its picture, which is
    /// exactly the failure this prevents.
    /// </remarks>
    private static string ResolveContentType(DataSourceSearchResult result)
    {
        if (!string.IsNullOrEmpty(result.ContentType) &&
            !string.Equals(result.ContentType, KnowledgeObjectTypes.Text, StringComparison.OrdinalIgnoreCase))
        {
            return result.ContentType;
        }

        if (string.IsNullOrEmpty(result.ReferenceId))
        {
            return result.ContentType;
        }

        foreach (var candidate in new[] { KnowledgeObjectTypes.Figure, KnowledgeObjectTypes.Chart, KnowledgeObjectTypes.Table })
        {
            if (result.ReferenceId.StartsWith(candidate + ':', StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return result.ContentType;
    }

    /// <summary>
    /// Renders the figure and table blocks, or nothing when there were none.
    /// </summary>
    /// <returns>The rendered blocks.</returns>
    public string Render()
    {
        if (_figures.Count == 0 && _tables.Count == 0)
        {
            return string.Empty;
        }

        using var builder = ZString.CreateStringBuilder();

        if (_figures.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Figures:");

            for (var index = 0; index < _figures.Count; index++)
            {
                var figure = _figures[index];

                builder.Append(figure.Label);
                builder.Append(' ');
                builder.Append(string.IsNullOrWhiteSpace(figure.Title) ? figure.Id : figure.Title);

                if (figure.Page.HasValue)
                {
                    builder.Append(" (p. ");
                    builder.Append(figure.Page.Value);
                    builder.Append(')');
                }

                // No address on this line, deliberately. A model handed a long opaque identifier does not
                // copy it — it copies the shape and substitutes its own ordinals, so an answer ends up
                // pointing at a figure that was never among the results, with an address that looks
                // exactly like a working one and resolves to nothing. The label below is registered
                // against the real address instead, and the host puts the picture where the label sits.

                if (string.Equals(figure.ValueConfidence, ChartValueConfidence.Descriptive, StringComparison.OrdinalIgnoreCase))
                {
                    // Said outright, because a number read off a picture by eye looks exactly like a
                    // number lifted from the file's own geometry and only one of them is true.
                    builder.Append("   values: descriptive - not machine-readable");
                }
                else if (!string.IsNullOrWhiteSpace(figure.ValueConfidence))
                {
                    builder.Append("   values: ");
                    builder.Append(figure.ValueConfidence);
                }
                else if (string.Equals(figure.ContentType, KnowledgeObjectTypes.Chart, StringComparison.OrdinalIgnoreCase))
                {
                    // Silence is not a claim of exactness. Not every index provider carries the flag
                    // back, and a chart whose numbers were lifted from the file's own geometry says so
                    // explicitly — so if nothing says so, the numbers are treated as read off by eye.
                    builder.Append("   values: unconfirmed - do not quote as exact");
                }

                builder.AppendLine();

                // Only ever populated for a chart whose values came from the document itself; the gate
                // sits in Collect, next to the confidence it reads. A chart that says it is exact and
                // then offers nothing to compute with is a picture the model can only describe.
                if (_figureSeries[index] is { Count: > 0 } series)
                {
                    var points = RenderSeries(series);

                    if (points is not null)
                    {
                        builder.AppendLine(points);
                    }
                }

                // Registered under the very label that was just written, and read back from the same
                // figure, so what the model is shown and what the host looks up cannot drift apart.
                RegisterImage(index, figure);
            }

            // The example is one of the labels actually printed above rather than an invented one, since
            // an example ordinal no figure carries is precisely the thing being guarded against.
            var example = _figures.FirstOrDefault(IsServable);
            var unservable = _figures.Where(figure => !IsServable(figure)).Select(figure => figure.Label).ToList();

            if (example is not null)
            {
                // The label is the whole instruction. Naming an address here would undo the point of
                // having a label at all, because a model told to show a picture writes whatever address
                // it was shown — near enough to look right, wrong often enough to 404.
                builder.Append("To show a figure in the answer, write its label exactly as printed above, on a line of its own - a line containing only ");
                builder.Append(example.Label);
                builder.AppendLine(". Never write a URL for a figure: an address you write yourself will not resolve.");
            }

            if (unservable.Count == _figures.Count)
            {
                // Said outright, because the alternative is worse than saying nothing. Told only that a
                // picture exists and given no way to show it, a model asked to show one will write a
                // plausible URL of its own invention, and the reader gets a broken image that looks like
                // a real citation.
                builder.AppendLine("These figures have no address this host can serve. Describe them; never invent a URL for one.");
            }
            else if (unservable.Count > 0)
            {
                // The ones that cannot be shown are named, or the instruction above reads as covering
                // every figure listed and a label the host cannot resolve reaches the reader as the raw
                // characters the model typed.
                builder.Append("These figures have no address this host can serve and cannot be shown: ");
                builder.Append(string.Join(", ", unservable));
                builder.AppendLine(". Describe them; never invent a URL for one.");
            }
        }

        if (_tables.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Tables:");

            foreach (var table in _tables)
            {
                builder.Append(table.Label);
                builder.Append(' ');
                builder.Append(string.IsNullOrWhiteSpace(table.Title) ? table.Id : table.Title);

                if (!string.IsNullOrWhiteSpace(table.Columns))
                {
                    builder.Append(" - columns: ");
                    builder.Append(table.Columns);
                }

                if (table.Page.HasValue)
                {
                    builder.Append(" (p. ");
                    builder.Append(table.Page.Value);
                    builder.Append(')');
                }

                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Determines whether the host can actually produce the picture behind a figure.
    /// </summary>
    /// <param name="figure">The figure.</param>
    /// <returns><see langword="true"/> when the figure has an address this host serves.</returns>
    private static bool IsServable(RetrievedFigure figure)
    {
        return !string.IsNullOrWhiteSpace(figure.Link);
    }

    /// <summary>
    /// Registers one figure on the invocation context under its own label, so the host can put the
    /// picture where the model wrote that label.
    /// </summary>
    /// <param name="index">The figure's position in the block, which is the number printed in its label.</param>
    /// <param name="figure">The figure.</param>
    /// <remarks>
    /// This is the mechanism a <c>[doc:n]</c> citation already uses — a reference keyed by the literal
    /// marker the model writes — with the client substituting a picture for the marker rather than a
    /// footnote. Only a figure the host can serve is registered: a label that resolves to nothing would
    /// reach the reader as raw text in the middle of a sentence, which is why it is never offered.
    /// </remarks>
    private void RegisterImage(int index, RetrievedFigure figure)
    {
        if (_invocationContext is null || !IsServable(figure))
        {
            return;
        }

        var origin = _figureOrigins[index];
        var caption = string.IsNullOrWhiteSpace(figure.Title) ? figure.Caption : figure.Title;

        _invocationContext.ToolReferences.TryAdd(figure.Label, new AICompletionReference
        {
            Text = string.IsNullOrWhiteSpace(caption) ? figure.Label : caption,
            Title = caption,
            Link = figure.Link,
            IsImage = true,
            Index = index + 1,
            ReferenceId = figure.Id,
            ReferenceType = origin.ReferenceType,
            DataSourceId = origin.DataSourceId,
        });
    }

    /// <summary>
    /// Reads the points stored with a chart row.
    /// </summary>
    /// <param name="value">The stored value.</param>
    /// <param name="referenceId">The row the value came from, so a value that cannot be read says which.</param>
    /// <returns>The series, or <see langword="null"/> when the row stored none or stored something unreadable.</returns>
    private List<ChartSeries> ParseSeries(string value, string referenceId)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<ChartSeries>>(value, _seriesJsonOptions);
        }
        catch (Exception ex)
        {
            // A warning rather than silence: the row claims its values were lifted from the document and
            // then hands back something that is not points, which is a fault in what was stored and not
            // an ordinary chart with nothing to offer. The figure still lists, and still says what its
            // confidence is; it simply has no numbers to show for it.
            _logger?.LogWarning(ex, "The stored chart series for '{ReferenceId}' could not be read, so no points are offered for it.", referenceId);

            return null;
        }
    }

    /// <summary>
    /// Renders a chart's points as data, capped so a block listing several charts stays a list of figures.
    /// </summary>
    /// <param name="series">The series read off the row.</param>
    /// <returns>The line to print, or <see langword="null"/> when no series carried a point.</returns>
    /// <remarks>
    /// Whatever is left out is named as left out. A silently shortened series is worse than no series at
    /// all, because a model shown the first stretch of a chart with nothing to say otherwise reads it as
    /// the whole chart and answers about the trend it can see.
    /// </remarks>
    private static string RenderSeries(List<ChartSeries> series)
    {
        var inlined = new List<ChartSeries>();
        var seriesWithPoints = 0;
        var totalPoints = 0;
        var inlinedPoints = 0;

        foreach (var entry in series)
        {
            if (entry?.Points is not { Count: > 0 })
            {
                continue;
            }

            seriesWithPoints++;
            totalPoints += entry.Points.Count;

            // Counted but not inlined, so the note below can say how much of the chart is missing rather
            // than only how much of it is here.
            if (inlined.Count == MaxInlinedSeries)
            {
                continue;
            }

            var points = entry.Points.Take(MaxInlinedPointsPerSeries).ToList();

            inlinedPoints += points.Count;

            // Whatever name the chart carried, which for a chart whose legend was never read is none.
            inlined.Add(new ChartSeries
            {
                Name = entry.Name,
                Points = points,
            });
        }

        if (inlined.Count == 0)
        {
            return null;
        }

        using var builder = ZString.CreateStringBuilder();

        builder.Append("   series (JSON): ");
        builder.Append(JsonSerializer.Serialize(inlined, _seriesJsonOptions));

        if (inlinedPoints < totalPoints || inlined.Count < seriesWithPoints)
        {
            builder.Append(" - truncated: showing the first ");
            builder.Append(inlinedPoints);
            builder.Append(" of ");
            builder.Append(totalPoints);
            builder.Append(" points");

            if (inlined.Count < seriesWithPoints)
            {
                builder.Append(" from ");
                builder.Append(inlined.Count);
                builder.Append(" of ");
                builder.Append(seriesWithPoints);
                builder.Append(" series");
            }

            builder.Append('.');
        }

        return builder.ToString();
    }

    private static string ReadFilter(DataSourceSearchResult result, string name)
    {
        if (result.Filters is null || !result.Filters.TryGetValue(name, out var value))
        {
            return null;
        }

        return value as string ?? value?.ToString();
    }

    /// <summary>
    /// Works out which reference type answers for a row, so its link and the reference registered for it
    /// are resolved the same way.
    /// </summary>
    /// <param name="result">The hit.</param>
    /// <returns>The reference type, or <see langword="null"/> when neither the row nor the data source names one.</returns>
    /// <remarks>
    /// The row states its own reference type when the index carries the column, and the data source it
    /// came from answers for it when the index does not. Reading only the column meant a figure stored in
    /// an index built before that column existed had no resolver, so no address — and a figure with no
    /// address is one the answer can only describe.
    /// </remarks>
    private string ResolveReferenceType(DataSourceSearchResult result)
    {
        return string.IsNullOrWhiteSpace(result.ReferenceType) ? _sourceType : result.ReferenceType;
    }

    /// <summary>
    /// Resolves the address a person can open for a figure, through the same link resolver citations use.
    /// </summary>
    /// <param name="result">The figure hit.</param>
    /// <returns>The link, or <see langword="null"/> when the host exposes none.</returns>
    /// <remarks>
    /// The logical <c>crestapps://</c> address is what an MCP client reads the picture through; it means
    /// nothing in a chat. The host's own download route does, and a citation on the same row already
    /// points at it, so the two agree by construction.
    /// </remarks>
    private string ResolveLink(DataSourceSearchResult result)
    {
        if (string.IsNullOrWhiteSpace(result.ReferenceId))
        {
            return null;
        }

        var referenceType = ResolveReferenceType(result);

        if (string.IsNullOrWhiteSpace(referenceType))
        {
            return null;
        }

        IAIReferenceLinkResolver resolver;

        try
        {
            resolver = _services.GetKeyedService<IAIReferenceLinkResolver>(referenceType);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "No link resolver could be obtained for reference type '{ReferenceType}'.", referenceType);

            return null;
        }

        if (resolver is null)
        {
            // Debug, not a warning: most source types have no figure endpoint and never will, so this
            // is the ordinary case for them rather than a misconfiguration.
            if (_logger?.IsEnabled(LogLevel.Debug) == true)
            {
                _logger.LogDebug("No link resolver is registered for reference type '{ReferenceType}', so figures have no address.", referenceType);
            }

            return null;
        }

        try
        {
            return resolver.ResolveLink(result.ReferenceId, new Dictionary<string, object>
            {
                ["Title"] = result.Title,
                ["DataSourceId"] = result.DataSourceId ?? _dataSourceId,
            });
        }
        catch (Exception ex)
        {
            // A link that cannot be built costs the picture's address in the answer, never the answer.
            _logger?.LogWarning(ex, "Failed to build a figure link for '{ReferenceId}'.", result.ReferenceId);

            return null;
        }
    }
}
