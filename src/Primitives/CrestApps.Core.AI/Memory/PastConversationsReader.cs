using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Memory;

/// <summary>
/// The user, profile and current session the past-conversation tools are scoped to, set by the orchestration
/// handler from the signed-in user for one invocation.
/// </summary>
internal sealed record PastConversationsScope(string UserId, string ProfileId, string CurrentSessionId);

/// <summary>
/// Reads the past conversations a <see cref="PastConversationsScope"/> may see: the scope user's own sessions
/// with the scope profile, other than the current one. Ownership is checked on every session read, not left to
/// the session store's query filter.
/// </summary>
internal static class PastConversationsReader
{
    public const int MaxCandidateSessions = 30;

    public static PastConversationsScope GetScope()
    {
        return AIInvocationScope.Current?.Items.TryGetValue(MemoryConstants.CompletionContextKeys.PastConversationsScope, out var value) == true
            ? value as PastConversationsScope
            : null;
    }

    public static async Task<List<AIChatSession>> GetSessionsAsync(
        IServiceProvider services,
        PastConversationsScope scope,
        int max,
        CancellationToken cancellationToken)
    {
        var manager = services.GetService<IAIChatSessionManager>();

        if (manager is null || scope is null || string.IsNullOrEmpty(scope.UserId) || string.IsNullOrEmpty(scope.ProfileId))
        {
            return [];
        }

        var page = await manager.PageAsync(1, max + 1, new AIChatSessionQueryContext
        {
            ProfileId = scope.ProfileId,
            UserId = scope.UserId,
            Sorted = true,
        }, cancellationToken);

        var sessions = new List<AIChatSession>();
        var outOfScope = 0;

        foreach (var entry in page?.Sessions ?? [])
        {
            if (string.Equals(entry.SessionId, scope.CurrentSessionId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!IsInScope(entry.UserId, entry.ProfileId, scope))
            {
                outOfScope++;

                continue;
            }

            var session = await manager.FindByIdAsync(entry.SessionId, cancellationToken);

            if (session is null || !IsInScope(session.UserId, session.ProfileId, scope))
            {
                outOfScope++;

                continue;
            }

            sessions.Add(session);

            if (sessions.Count == max)
            {
                break;
            }
        }

        var logger = services.GetService<ILoggerFactory>()?.CreateLogger(typeof(PastConversationsReader));

        if (outOfScope > 0)
        {
            // The store ignored the user or profile filter; nothing leaked, but the store should be fixed.
            logger?.LogWarning(
                "The chat session store returned {Count} session(s) of another user or profile for a past-conversations lookup; they were left out.",
                outOfScope);
        }

        if (logger?.IsEnabled(LogLevel.Debug) == true)
        {
            logger.LogDebug(
                "Past conversations for user '{UserId}' and profile '{ProfileId}': {Count} conversation(s) in scope.",
                scope.UserId,
                scope.ProfileId,
                sessions.Count);
        }

        return sessions;
    }

    public static async Task<List<ChatMessage>> GetMessagesAsync(IServiceProvider services, AIChatSession session, CancellationToken cancellationToken)
    {
        var promptStore = services.GetService<IAIChatSessionPromptStore>();

        if (promptStore is null)
        {
            return [];
        }

        var prompts = await promptStore.GetPromptsAsync(session.SessionId);

        return ConversationRecall.GetEligibleMessages(prompts
            .Where(prompt => !prompt.IsGeneratedPrompt && string.Equals(prompt.SessionId, session.SessionId, StringComparison.Ordinal))
            .OrderBy(prompt => prompt.CreatedUtc)
            .Select(prompt => new ChatMessage(prompt.Role, prompt.Content)));
    }

    private static bool IsInScope(string userId, string profileId, PastConversationsScope scope)
    {
        return string.Equals(userId, scope.UserId, StringComparison.Ordinal) &&
            string.Equals(profileId, scope.ProfileId, StringComparison.Ordinal);
    }
}
