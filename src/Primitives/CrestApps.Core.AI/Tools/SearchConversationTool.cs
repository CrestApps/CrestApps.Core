using System.Text.Json;
using CrestApps.Core.AI.Extensions;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Speech;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Tools;

/// <summary>
/// Searches the earlier part of the current conversation: the messages that no longer fit the past-messages
/// window. It reads only the messages the orchestration handler placed on the invocation scope, so it can only
/// ever see the conversation it is running in.
/// </summary>
public sealed class SearchConversationTool : AIFunction
{
    public const string TheName = "search_conversation";

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "query": {
          "type": "string",
          "description": "Words describing what was said earlier, such as a name, figure, topic or decision."
        }
      },
      "required": [
        "query"
      ],
      "additionalProperties": false
    }
    """);

    public override string Name => TheName;

    public override string Description => "Searches the earlier part of this conversation that is no longer in your message history, and returns the matching turns verbatim.";

    public override JsonElement JsonSchema => _jsonSchema;

    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } =
        new Dictionary<string, object>()
        {
            ["Strict"] = false,
        };

    protected override ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (!arguments.TryGetFirstString("query", out var query))
        {
            return ValueTask.FromResult<object>("A 'query' argument is required.");
        }

        if (AIInvocationScope.Current?.Items.TryGetValue(MemoryConstants.CompletionContextKeys.OlderConversationMessages, out var value) != true ||
            value is not List<ChatMessage> messages ||
            messages.Count == 0)
        {
            return ValueTask.FromResult<object>("The whole conversation is already in your message history; there is nothing earlier to search.");
        }

        var tokenizer = arguments.Services.GetRequiredService<ITextTokenizer>();
        var matches = ConversationRecall.Search(tokenizer, ConversationRecall.GetTurns(messages), [query], top: 5, minScore: 0.2f);
        var logger = arguments.Services.GetService<ILogger<SearchConversationTool>>();

        if (logger?.IsEnabled(LogLevel.Debug) == true)
        {
            logger.LogDebug("AI tool '{ToolName}' matched {MatchCount} earlier turn(s) of this conversation.", Name, matches.Count);
        }

        if (matches.Count == 0)
        {
            return ValueTask.FromResult<object>("Nothing earlier in this conversation matches that query.");
        }

        return ValueTask.FromResult<object>(JsonSerializer.Serialize(matches.Select(turn => new
        {
            turn = turn.Number,
            user = turn.UserText,
            assistant = turn.AssistantText,
        })));
    }
}
