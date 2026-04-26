using System.Text.Json;
using CrestApps.Core.AI.Extensions;
using CrestApps.Core.AI.Memory;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Tools;

/// <summary>
/// Represents the list User Memories Tool.
/// </summary>
public sealed class ListUserMemoriesTool : AIFunction
{
    public const string TheName = "list_user_memories";

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "limit": {
          "type": "integer",
          "description": "Maximum number of memories to return."
        }
      },
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
    public override string Description => "Lists the current authenticated user's private memories.";

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

    protected override async ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var logger = arguments.Services.GetRequiredService<ILogger<ListUserMemoriesTool>>();
        var userId = AIMemoryToolHelpers.GetCurrentUserId(arguments.Services);

        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("AI tool '{ToolName}' requires an authenticated user.", Name);
            return "User memory is only available for authenticated users.";
        }

        var limit = arguments.GetFirstValueOrDefault("limit", 25);
        limit = Math.Clamp(limit, 1, 100);

        var store = arguments.Services.GetRequiredService<IAIMemoryStore>();
        var memories = await store.GetByUserAsync(userId, limit);

        if (memories.Count == 0)
        {
            return "No user memories have been saved yet.";
        }

        return JsonSerializer.Serialize(memories.Select(x => new
        {
            x.ItemId,
            x.Name,
            x.Description,
            x.Content,
            x.CreatedUtc,
            x.UpdatedUtc,
        }));
    }
}
