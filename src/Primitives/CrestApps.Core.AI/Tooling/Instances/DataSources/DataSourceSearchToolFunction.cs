using System.Text.Json;
using CrestApps.Core.AI.Extensions;
using CrestApps.Core.AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Tooling.Instances.DataSources;

/// <summary>
/// An <see cref="AIFunction"/> produced by the data source search tool instance source. Each function is
/// bound to a single configured <see cref="CrestApps.Core.AI.Models.AIDataSource"/> and searches only that
/// data source, so a host can expose one callable function per knowledge base it wants the model to reach.
/// </summary>
/// <remarks>
/// The model supplies only the search phrases — one, or up to <see cref="DataSourceRetrieval.MaxQueries"/>
/// when the question spans genuinely distinct topics. They are embedded in a single batched call using the
/// same embedding deployment the data source's knowledge base index was indexed with, so they are compared
/// against the stored chunks in the same vector space, then searched in parallel and fused into one ranking.
/// Every other retrieval parameter — retrieval mode, retrieved document count, strictness, and filter —
/// comes from the instance settings the user configured.
/// </remarks>
public sealed class DataSourceSearchToolFunction : AIFunction
{
    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "queries": {
          "type": "array",
          "items": { "type": "string" },
          "minItems": 1,
          "maxItems": 3,
          "description": "One search phrase per genuinely distinct topic the question covers. Each phrase is a short set of keywords (nouns and distinctive words), not the user's full sentence. Pass a SINGLE phrase unless the question really spans separate subjects — for example ['vacation policy', 'sick leave policy'] for a question comparing the two. Never pass synonyms, rewordings, or singular/plural variants of the same idea: they retrieve the same content twice and waste the search budget."
        }
      },
      "required": ["queries"],
      "additionalProperties": false
    }
    """);

    private readonly string _name;
    private readonly string _description;
    private readonly DataSourceSearchToolSettings _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataSourceSearchToolFunction"/> class.
    /// </summary>
    /// <param name="name">The function name exposed to the AI model.</param>
    /// <param name="description">The description exposed to the AI model.</param>
    /// <param name="settings">The configured instance settings applied to every search.</param>
    public DataSourceSearchToolFunction(string name, string description, DataSourceSearchToolSettings settings)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(settings);

        _name = name;
        _description = string.IsNullOrWhiteSpace(description)
            ? name
            : description;
        _settings = settings;
    }

    /// <summary>
    /// Gets the function name exposed to the AI model.
    /// </summary>
    public override string Name => _name;

    /// <summary>
    /// Gets the description exposed to the AI model.
    /// </summary>
    public override string Description => _description;

    /// <summary>
    /// Gets the JSON schema describing the arguments the model may supply.
    /// </summary>
    public override JsonElement JsonSchema => _jsonSchema;

    /// <summary>
    /// Gets additional metadata applied to the function.
    /// </summary>
    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } = new Dictionary<string, object>
    {
        ["Strict"] = false,
    };

    /// <summary>
    /// Searches the configured data source for the supplied query.
    /// </summary>
    /// <param name="arguments">The arguments supplied by the AI model.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var services = arguments.Services;
        var logger = services?.GetService<ILoggerFactory>()?.CreateLogger<DataSourceSearchToolFunction>()
            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DataSourceSearchToolFunction>.Instance;

        var queries = ReadQueries(arguments);

        if (queries.Count == 0)
        {
            logger.LogWarning("AI tool '{ToolName}' missing required argument 'queries'.", _name);

            return "Unable to find a 'queries' argument in the arguments parameter.";
        }

        if (services is null)
        {
            return "No services are available to search the data source.";
        }

        if (string.IsNullOrEmpty(_settings.DataSourceId))
        {
            logger.LogWarning("AI tool '{ToolName}' failed: no data source is configured for this instance.", _name);

            return "No data source is configured for this tool. Configure one in the tool instance settings.";
        }

        try
        {
            return await DataSourceRetrieval.SearchAsync(
                services,
                new DataSourceRetrievalRequest
                {
                    DataSourceId = _settings.DataSourceId,
                    Queries = queries,
                    TopNDocuments = _settings.TopNDocuments,
                    Strictness = _settings.Strictness,
                    Filter = _settings.Filter,
                    RetrievalMode = _settings.RetrievalMode,
                },
                _name,
                logger,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI tool '{ToolName}' failed to search data source '{DataSourceId}'.", _name, _settings.DataSourceId);

            return "An error occurred while searching the data source.";
        }
    }

    /// <summary>
    /// Reads the search phrases the model supplied.
    /// </summary>
    /// <remarks>
    /// The schema asks for an array under <c>queries</c>, but models routinely send a bare string, and some
    /// send the singular <c>query</c> the rest of the tooling uses. Both are accepted: refusing them would
    /// turn a trivially recoverable shape mismatch into a wasted tool call the model has to diagnose.
    /// </remarks>
    /// <param name="arguments">The arguments supplied by the AI model.</param>
    /// <returns>The phrases to search for, in the order supplied.</returns>
    private static List<string> ReadQueries(AIFunctionArguments arguments)
    {
        var queries = new List<string>();

        foreach (var key in (string[])["queries", "query"])
        {
            if (!arguments.TryGetFirst(key, out var raw))
            {
                continue;
            }

            Collect(raw, queries);

            if (queries.Count > 0)
            {
                break;
            }
        }

        return queries;
    }

    private static void Collect(object value, List<string> queries)
    {
        switch (value)
        {
            case string text:
                Add(text, queries);

                break;

            case JsonElement { ValueKind: JsonValueKind.String } element:
                Add(element.GetString(), queries);

                break;

            case JsonElement { ValueKind: JsonValueKind.Array } element:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, queries);
                }

                break;

            case IEnumerable<string> texts:
                foreach (var text in texts)
                {
                    Add(text, queries);
                }

                break;

            case System.Collections.IEnumerable items:
                foreach (var item in items)
                {
                    Collect(item, queries);
                }

                break;

            default:
                Add(value?.ToString(), queries);

                break;
        }
    }

    private static void Add(string text, List<string> queries)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            queries.Add(text);
        }
    }
}
