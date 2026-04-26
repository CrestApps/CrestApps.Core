using System.Text.Json;
using CrestApps.Core.AI.Extensions;
using CrestApps.Core.AI.Memory;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Tools;

/// <summary>
/// Represents the search User Memories Tool.
/// </summary>
public sealed class SearchUserMemoriesTool : AIFunction
{
    public const string TheName = "search_user_memories";

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "query": {
          "type": "string",
          "description": "The memory search query."
        },
        "top_n": {
          "type": "integer",
          "description": "Maximum number of matching memories to return."
        }
      },
      "required": [
        "query"
      ],
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
    public override string Description => "Searches the current authenticated user's private memories for relevant preferences, projects, recurring topics, interests, and other durable details.";

    /// <summary>
    /// Gets the json Schema.
    /// </summary>
    public override JsonElement JsonSchema => _jsonSchema;

    /// <summary>
    /// Gets the additional Properties.
    /// </summary>
    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } =
        new Dictionary<string, object>()
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
        var logger = arguments.Services.GetRequiredService<ILogger<SearchUserMemoriesTool>>();

        if (!arguments.TryGetFirstString("query", out var query))
        {
            logger.LogWarning("AI tool '{ToolName}' missing required argument 'query'.", Name);
            return "Unable to find a 'query' argument in the arguments parameter.";
        }

        var userId = AIMemoryToolHelpers.GetCurrentUserId(arguments.Services);

        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("AI tool '{ToolName}' requires an authenticated user.", Name);
            return "User memory is only available for authenticated users.";
        }

        var memorySearchService = arguments.Services.GetRequiredService<IAIMemorySearchService>();
        var results = await memorySearchService.SearchAsync(
            userId,
            [query],
            arguments.GetFirstValueOrDefault<int?>("top_n", null),
            cancellationToken);

        if (!results.Any())
        {
            return "No relevant user memories were found for this query.";
        }

        return JsonSerializer.Serialize(results.Select(x => new
        {
            x.MemoryId,
            x.Name,
            x.Description,
            x.Content,
            x.UpdatedUtc,
            x.Score,
        }));
    }
}
