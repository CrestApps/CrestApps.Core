using System.Text.Json;
using CrestApps.Core.AI.Extensions;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Speech;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Tools;

/// <summary>
/// Searches the signed-in user's earlier conversations with the current profile for what they say about a query.
/// </summary>
public sealed class SearchPastConversationsTool : AIFunction
{
    public const string TheName = "search_past_conversations";

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "query": {
          "type": "string",
          "description": "Words describing what the user is referring to, such as a topic, name, figure or decision."
        },
        "top_n": {
          "type": "integer",
          "description": "How many conversations to return, from 1 to 5. Defaults to 3."
        }
      },
      "required": [
        "query"
      ],
      "additionalProperties": false
    }
    """);

    public override string Name => TheName;

    public override string Description => "Searches the user's earlier conversations with you and returns the ones that discuss the query, with when they happened and the matching turns.";

    public override JsonElement JsonSchema => _jsonSchema;

    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } =
        new Dictionary<string, object>()
        {
            ["Strict"] = false,
        };

    protected override async ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (!arguments.TryGetFirstString("query", out var query))
        {
            return "A 'query' argument is required.";
        }

        var scope = PastConversationsReader.GetScope();

        if (scope is null)
        {
            return "Past conversations are only available to signed-in users when they are turned on for this assistant.";
        }

        var topN = Math.Clamp(arguments.GetFirstValueOrDefault<int?>("top_n", null) ?? 3, 1, 5);
        var tokenizer = arguments.Services.GetRequiredService<ITextTokenizer>();
        var sessions = await PastConversationsReader.GetSessionsAsync(arguments.Services, scope, PastConversationsReader.MaxCandidateSessions, cancellationToken);
        var matches = new List<(AIChatSession Session, string Summary, List<ConversationTurn> Turns, float Score)>();

        foreach (var session in sessions)
        {
            var summary = session.TryGet<AIConversationMemory>(out var memory) ? memory.Summary : null;
            var turns = ConversationRecall.GetTurns(await PastConversationsReader.GetMessagesAsync(arguments.Services, session, cancellationToken));
            var matchingTurns = ConversationRecall.Search(tokenizer, turns, [query], top: 2, minScore: 0.2f);

            // The title and summary count too: a summarized conversation may no longer say it in so many words.
            var overview = ConversationRecall.Search(
                tokenizer,
                [new ConversationTurn { UserText = session.Title, AssistantText = summary }],
                [query],
                top: 1,
                minScore: 0.2f);

            var score = matchingTurns.Select(turn => turn.Score).Concat(overview.Select(turn => turn.Score)).DefaultIfEmpty(0).Max();

            if (score > 0)
            {
                matches.Add((session, summary, matchingTurns, score));
            }
        }

        var logger = arguments.Services.GetService<ILogger<SearchPastConversationsTool>>();

        if (logger?.IsEnabled(LogLevel.Debug) == true)
        {
            logger.LogDebug("AI tool '{ToolName}' matched {MatchCount} of {SessionCount} past conversation(s).", Name, matches.Count, sessions.Count);
        }

        if (matches.Count == 0)
        {
            return "No earlier conversation with this user matches that query.";
        }

        return JsonSerializer.Serialize(matches
            .OrderByDescending(match => match.Score)
            .ThenByDescending(match => match.Session.LastActivityUtc)
            .Take(topN)
            .Select(match => new
            {
                title = match.Session.Title,
                startedUtc = match.Session.CreatedUtc,
                lastActivityUtc = match.Session.LastActivityUtc,
                summary = match.Summary,
                matchingTurns = match.Turns.Select(turn => new
                {
                    user = turn.UserText,
                    assistant = turn.AssistantText,
                }),
            }));
    }
}
