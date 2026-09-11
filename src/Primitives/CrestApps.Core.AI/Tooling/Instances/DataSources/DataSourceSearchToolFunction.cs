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
/// The model supplies only the search phrase. That phrase is embedded with the same embedding deployment
/// the data source's knowledge base index was indexed with, so it is compared against the stored chunks in
/// the same vector space. Every other retrieval parameter — retrieval mode, retrieved document count,
/// strictness, and filter — comes from the instance settings the user configured.
/// </remarks>
public sealed class DataSourceSearchToolFunction : AIFunction
{
    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "query": {
          "type": "string",
          "description": "A short set of keywords describing what to look for, extracted from the user's request. Pass the essential search terms (nouns and distinctive words), not the user's full sentence or question."
        }
      },
      "required": ["query"],
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

        if (!arguments.TryGetFirstString("query", out var query) || string.IsNullOrWhiteSpace(query))
        {
            logger.LogWarning("AI tool '{ToolName}' missing required argument 'query'.", _name);

            return "Unable to find a 'query' argument in the arguments parameter.";
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
                    Query = query,
                    TopNDocuments = _settings.TopNDocuments,
                    Strictness = _settings.Strictness,
                    Filter = _settings.Filter,
                    RetrievalMode = _settings.RetrievalMode,
                    IsInScope = _settings.IsInScope,
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
}
