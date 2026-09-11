using System.Text.Json;
using CrestApps.Core.AI.Extensions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Services;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Tools;

/// <summary>
/// Performs vector search against the configured data source knowledge base and returns relevant chunks with citations.
/// </summary>
public sealed class DataSourceSearchTool : AIFunction
{
    public const string TheName = SystemToolNames.SearchDataSources;

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "query": {
          "type": "string",
          "description": "The search query to find relevant content in the data source."
        }
      },
      "required": ["query"],
      "additionalProperties": false
    }
    """);

    /// <summary>
    /// Gets the name.
    /// </summary>
    public override string Name => TheName;

    /// <summary>
    /// Gets the description.
    /// </summary>
    public override string Description => "Searches configured data sources using semantic vector search and returns the most relevant text chunks with citations. Use this tool to answer questions based on the configured data source.";

    /// <summary>
    /// Gets the json Schema.
    /// </summary>
    public override JsonElement JsonSchema => _jsonSchema;

    /// <summary>
    /// Gets the additional Properties.
    /// </summary>
    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } =
        new Dictionary<string, object>
        {
            ["Strict"] = false,
        };

    /// <summary>
    /// Invoke cores core.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var logger = arguments.Services.GetRequiredService<ILogger<DataSourceSearchTool>>();

        if (!arguments.TryGetFirstString("query", out var query))
        {
            logger.LogWarning("AI tool '{ToolName}' missing required argument 'query'.", Name);

            return "Unable to find a 'query' argument in the arguments parameter.";
        }

        try
        {
            var invocationContext = AIInvocationScope.Current;
            var executionContext = invocationContext?.ToolExecutionContext;

            if (executionContext == null)
            {
                logger.LogWarning("AI tool '{ToolName}' failed: no active AI execution context.", Name);

                return "Data source search requires an active AI execution context.";
            }

            var dataSourceId = invocationContext.DataSourceId;

            if (string.IsNullOrEmpty(dataSourceId))
            {
                logger.LogWarning("AI tool '{ToolName}' failed: no data source configured for this profile.", Name);

                return "No data source is configured for this profile.";
            }

            var ragMetadata = GetRagMetadata(executionContext);

            return await DataSourceRetrieval.SearchAsync(
                arguments.Services,
                new DataSourceRetrievalRequest
                {
                    DataSourceId = dataSourceId,
                    Queries = [query],
                    TopNDocuments = ragMetadata?.TopNDocuments,
                    Strictness = ragMetadata?.Strictness,
                    Filter = ragMetadata?.Filter,
                    IsInScope = ragMetadata?.IsInScope == true,
                },
                Name,
                logger,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during data source search.");

            return "An error occurred while searching the data source.";
        }
    }

    private static AIDataSourceRagMetadata GetRagMetadata(AIToolExecutionContext executionContext)
    {
        if (executionContext.Resource is AIProfile profile && profile.TryGet<AIDataSourceRagMetadata>(out var ragMetadata))
        {
            return ragMetadata;
        }

        return null;
    }
}
