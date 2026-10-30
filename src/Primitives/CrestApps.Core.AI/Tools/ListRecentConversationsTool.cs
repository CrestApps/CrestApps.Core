using System.Text.Json;
using CrestApps.Core.AI.Extensions;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Tools;

/// <summary>
/// Lists the signed-in user's most recent earlier conversations with the current profile.
/// </summary>
public sealed class ListRecentConversationsTool : AIFunction
{
    public const string TheName = "list_recent_conversations";

    private const int PreviewLength = 300;

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "count": {
          "type": "integer",
          "description": "How many recent conversations to return, from 1 to 10. Defaults to 5."
        }
      },
      "additionalProperties": false
    }
    """);

    public override string Name => TheName;

    public override string Description => "Lists the user's most recent earlier conversations with you, newest first, with when they happened and what they were about.";

    public override JsonElement JsonSchema => _jsonSchema;

    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } =
        new Dictionary<string, object>()
        {
            ["Strict"] = false,
        };

    protected override async ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var scope = PastConversationsReader.GetScope();

        if (scope is null)
        {
            return "Past conversations are only available to signed-in users when they are turned on for this assistant.";
        }

        var count = Math.Clamp(arguments.GetFirstValueOrDefault<int?>("count", null) ?? 5, 1, 10);
        var sessions = await PastConversationsReader.GetSessionsAsync(arguments.Services, scope, count, cancellationToken);

        if (sessions.Count == 0)
        {
            return "There are no earlier conversations with this user.";
        }

        var results = new List<object>();

        foreach (var session in sessions)
        {
            string summary = null;
            string firstUserMessage = null;
            string lastAssistantMessage = null;

            if (session.TryGet<AIConversationMemory>(out var memory) && !string.IsNullOrWhiteSpace(memory.Summary))
            {
                summary = memory.Summary;
            }
            else
            {
                var messages = await PastConversationsReader.GetMessagesAsync(arguments.Services, session, cancellationToken);
                firstUserMessage = Preview(messages.FirstOrDefault(message => message.Role == ChatRole.User)?.Text);
                lastAssistantMessage = Preview(messages.LastOrDefault(message => message.Role == ChatRole.Assistant)?.Text);
            }

            results.Add(new
            {
                title = session.Title,
                startedUtc = session.CreatedUtc,
                lastActivityUtc = session.LastActivityUtc,
                summary,
                firstUserMessage,
                lastAssistantMessage,
            });
        }

        return JsonSerializer.Serialize(results);
    }

    private static string Preview(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= PreviewLength)
        {
            return text;
        }

        return string.Concat(text.AsSpan(0, PreviewLength), "…");
    }
}
