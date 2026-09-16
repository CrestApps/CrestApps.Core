using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Documents.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Tooling;

/// <summary>
/// Reads one knowledge object in full by the identifier a search returned.
/// </summary>
/// <remarks>
/// A search returns what matched, which for a long table or a transcribed figure is a summary of it. This is
/// how a model goes back for the whole thing once it knows which one it wants, without searching again and
/// hoping the same row comes back.
/// </remarks>
public sealed class KnowledgeObjectToolFunction : AIFunction
{
    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "id": {
          "type": "string",
          "description": "The identifier of the object to read, exactly as a previous search reported it - for example 'figure:0123456789abcdef:1:2' or 'table:0123456789abcdef:1:0'."
        }
      },
      "required": ["id"],
      "additionalProperties": false
    }
    """);

    // A chart states what it knows and stays silent about the rest: an axis nobody titled and a series
    // nobody named are left out rather than written as null, because an absent name is not a fact about
    // the chart and a model reading one is owed neither.
    private static readonly JsonSerializerOptions _chartJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _name;
    private readonly string _description;
    private readonly KnowledgeObjectToolSettings _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="KnowledgeObjectToolFunction"/> class.
    /// </summary>
    /// <param name="name">The function name exposed to the AI model.</param>
    /// <param name="description">The description exposed to the AI model.</param>
    /// <param name="settings">The configured instance settings.</param>
    public KnowledgeObjectToolFunction(string name, string description, KnowledgeObjectToolSettings settings)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(settings);

        _name = name;
        _description = string.IsNullOrWhiteSpace(description) ? name : description;
        _settings = settings;
    }

    /// <inheritdoc />
    public override string Name => _name;

    /// <inheritdoc />
    public override string Description => _description;

    /// <inheritdoc />
    public override JsonElement JsonSchema => _jsonSchema;

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } = new Dictionary<string, object>
    {
        ["Strict"] = false,
    };

    /// <summary>
    /// Reads the requested object.
    /// </summary>
    /// <param name="arguments">The arguments supplied by the AI model.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The object, or a message explaining why it could not be read.</returns>
    protected override async ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var services = arguments.Services;
        var logger = services?.GetService<ILoggerFactory>()?.CreateLogger<KnowledgeObjectToolFunction>()
            ?? NullLogger<KnowledgeObjectToolFunction>.Instance;

        var id = ReadId(arguments);

        if (string.IsNullOrWhiteSpace(id))
        {
            return "Unable to find an 'id' argument in the arguments parameter.";
        }

        if (services is null)
        {
            return "No services are available to read the object.";
        }

        if (string.IsNullOrEmpty(_settings.DataSourceId))
        {
            logger.LogWarning("AI tool '{ToolName}' failed: no data source is configured for this instance.", _name);

            return "No data source is configured for this tool. Configure one in the tool instance settings.";
        }

        if (!IsKnownPrefix(id))
        {
            return $"'{id}' is not a knowledge object identifier. Use one exactly as a search reported it.";
        }

        var store = services.GetService<IKnowledgeObjectStore>();

        if (store is null)
        {
            return "Knowledge objects are not available in this application.";
        }

        KnowledgeObject entry;

        try
        {
            entry = await store.FindByCanonicalIdAsync(_settings.DataSourceId, id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI tool '{ToolName}' failed to read object '{CanonicalId}'.", _name, id);

            return "An error occurred while reading the object.";
        }

        // The identifier says nothing about which data source it belongs to, so the answer has to. An object
        // from another data source reads as not found, never as a result.
        if (entry is null || !string.Equals(entry.Source, _settings.DataSourceId, StringComparison.OrdinalIgnoreCase))
        {
            return $"No object with identifier '{id}' was found in this data source.";
        }

        if (entry.ObjectType is not (KnowledgeObjectTypes.Figure or KnowledgeObjectTypes.Chart) ||
            string.IsNullOrWhiteSpace(entry.StoragePath))
        {
            return new KnowledgeObjectToolResult
            {
                Text = BuildText(entry, link: null),
            };
        }

        var maxBytes = services.GetService<IOptions<ChatDocumentsOptions>>()?.Value.MaxVisionImageBytesPerFile ?? 0;
        var uri = $"crestapps://datasource/{_settings.DataSourceId}/figure/{entry.CanonicalId}";
        var link = ResolveLink(services, entry);
        var text = BuildText(entry, link);
        var content = await ReadPictureAsync(services, entry, maxBytes, logger, cancellationToken);

        return new KnowledgeObjectToolResult
        {
            Text = text,
            Content = content,
            MediaType = entry.MediaType,
            Uri = uri,
        };
    }

    /// <summary>
    /// Reads the picture, unless it is larger than a vision call is allowed to carry.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <param name="entry">The figure.</param>
    /// <param name="maxBytes">The per-file ceiling, or zero when there is none.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bytes, or empty when the picture is too large or unreadable. The caller then links to it.</returns>
    private static async Task<ReadOnlyMemory<byte>> ReadPictureAsync(
        IServiceProvider services,
        KnowledgeObject entry,
        long maxBytes,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var fileStore = services.GetService<IDocumentFileStore>();

        if (fileStore is null)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        try
        {
            await using var stream = await fileStore.GetFileAsync(entry.StoragePath);

            if (stream is null)
            {
                return ReadOnlyMemory<byte>.Empty;
            }

            if (maxBytes > 0 && stream.CanSeek && stream.Length > maxBytes)
            {
                // Returning it inline would blow the model's own image budget. The link still gets the
                // caller to the picture.
                return ReadOnlyMemory<byte>.Empty;
            }

            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);

            if (maxBytes > 0 && buffer.Length > maxBytes)
            {
                return ReadOnlyMemory<byte>.Empty;
            }

            return buffer.ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read the stored figure '{StoragePath}'.", entry.StoragePath);

            return ReadOnlyMemory<byte>.Empty;
        }
    }

    /// <summary>
    /// Resolves the address a person can open for a figure, through the same link resolver citations use.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <param name="entry">The figure.</param>
    /// <returns>The link, or <see langword="null"/> when the host exposes none.</returns>
    private static string ResolveLink(IServiceProvider services, KnowledgeObject entry)
    {
        var resolver = services.GetKeyedService<IAIReferenceLinkResolver>(AIDataSourceSourceTypes.File);

        if (resolver is null)
        {
            return null;
        }

        try
        {
            return resolver.ResolveLink(entry.CanonicalId, new Dictionary<string, object>
            {
                ["Title"] = entry.Title,
                ["DataSourceId"] = entry.Source,
            });
        }
        catch (Exception)
        {
            // A link that cannot be built costs the picture's address, never the picture.
            return null;
        }
    }

    /// <summary>
    /// Renders the object as text, with whatever its type adds beyond its content.
    /// </summary>
    /// <param name="entry">The object.</param>
    /// <param name="link">The address a person can open for a figure, or <see langword="null"/>.</param>
    /// <returns>The text.</returns>
    private static string BuildText(KnowledgeObject entry, string link)
    {
        var builder = new StringBuilder();

        builder.Append(entry.ObjectType).Append(": ").AppendLine(entry.CanonicalId);

        if (!string.IsNullOrWhiteSpace(entry.Title))
        {
            builder.Append("Title: ").AppendLine(entry.Title);
        }

        if (!string.IsNullOrWhiteSpace(link))
        {
            // The picture can be shown in the answer as a markdown image on this link.
            builder.Append("Image: ").AppendLine(link);
        }

        if (entry.PageStart.HasValue)
        {
            builder.Append("Page: ").Append(entry.PageStart.Value);

            if (entry.PageEnd.HasValue && entry.PageEnd.Value != entry.PageStart.Value)
            {
                builder.Append('-').Append(entry.PageEnd.Value);
            }

            builder.AppendLine();
        }

        if (entry.TryGet<ChartDetails>(out var chart) && !string.IsNullOrWhiteSpace(chart.ValueConfidence))
        {
            builder.Append("Values: ").AppendLine(
                chart.ValueConfidence == ChartValueConfidence.Descriptive
                    ? "descriptive - not machine-readable"
                    : chart.ValueConfidence);
        }

        builder.AppendLine();
        builder.AppendLine(entry.Content);

        if (entry.ObjectType == KnowledgeObjectTypes.Table && entry.TryGet<TableDetails>(out var table) && table.Rows is { Count: > 0 })
        {
            // The rows are handed back as data rather than prose, so a caller that wants to compute with
            // them does not have to parse a sentence back into numbers.
            builder.AppendLine();
            builder.AppendLine("Rows (JSON):");
            builder.AppendLine(JsonSerializer.Serialize(new
            {
                columns = table.Columns,
                rows = table.Rows,
            }));
        }

        if (chart is not null && chart.ValueConfidence == ChartValueConfidence.Exact && chart.Series is { Count: > 0 })
        {
            // The points get the same treatment the rows above get, and for the same reason: a caller that
            // wants to compute with them should not have to parse a sentence back into numbers. Only at
            // exact confidence, though. At any other level the values were estimated off a picture, and an
            // estimate written out as JSON reads exactly like a measurement - which is the one thing the
            // confidence is carried to prevent. The line above has already said so, and says it alone.
            builder.AppendLine();
            builder.AppendLine("Series (JSON):");
            builder.AppendLine(JsonSerializer.Serialize(new
            {
                chartType = chart.ChartType,
                axisX = chart.AxisX,
                axisY = chart.AxisY,
                series = chart.Series,
            }, _chartJsonOptions));
        }

        return builder.ToString().TrimEnd();
    }

    private static bool IsKnownPrefix(string id)
    {
        foreach (var prefix in new[]
        {
            KnowledgeObjectTypes.Document,
            KnowledgeObjectTypes.Article,
            KnowledgeObjectTypes.Text,
            KnowledgeObjectTypes.Figure,
            KnowledgeObjectTypes.Chart,
            KnowledgeObjectTypes.Table,
        })
        {
            if (id.StartsWith(prefix + ':', StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ReadId(AIFunctionArguments arguments)
    {
        if (!arguments.TryGetValue("id", out var raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            string value => value.Trim(),
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString()?.Trim(),
            _ => raw.ToString()?.Trim(),
        };
    }
}
