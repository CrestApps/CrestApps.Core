using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Chat;

/// <summary>
/// The chat hub's logs that follow a message from the moment it arrives until it is committed. Each has an event id,
/// so one session's persistence can be filtered without matching on text. The ids sit in the chat range (3000s).
/// </summary>
internal static partial class ChatLog
{
    [LoggerMessage(EventId = 3001, Level = LogLevel.Debug, Message = "[ChatPersist] Resolved session {SessionId} (isNew={IsNew}) on connection {ConnectionId} using services scope {ServicesHash}.")]
    public static partial void SessionResolved(ILogger logger, string sessionId, bool isNew, string connectionId, int servicesHash);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Debug, Message = "[ChatPersist] Staged user prompt {ItemId} for session {SessionId}; store now reports {StagedCount} prompt(s) (promptStore {StoreHash}).")]
    public static partial void UserPromptStaged(ILogger logger, string itemId, string sessionId, int stagedCount, int storeHash);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Debug, Message = "[ChatPersist] Staged assistant prompt {ItemId} for session {SessionId}.")]
    public static partial void AssistantPromptStaged(ILogger logger, string itemId, string sessionId);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Debug, Message = "[ChatPersist] Session {SessionId} has {StagedCount} prompt(s) staged before final commit.")]
    public static partial void PromptsStagedBeforeCommit(ILogger logger, string sessionId, int stagedCount);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Debug, Message = "[ChatPersist] Committing session {SessionId} using committer {CommitterHash} resolved from services scope {ServicesHash}.")]
    public static partial void SessionCommitting(ILogger logger, string sessionId, int committerHash, int servicesHash);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Debug, Message = "[ChatPersist] After commit, session {SessionId} reports {PersistedCount} persisted prompt(s) (promptStore {StoreHash}).")]
    public static partial void SessionCommitted(ILogger logger, string sessionId, int persistedCount, int storeHash);
}
