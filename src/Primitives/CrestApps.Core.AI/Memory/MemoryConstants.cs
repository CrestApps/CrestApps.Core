namespace CrestApps.Core.AI.Memory;

/// <summary>
/// Provides functionality for memory Constants.
/// </summary>
public static class MemoryConstants
{
    public const string IndexingTaskType = "AIMemory";

    /// <summary>
    /// Provides functionality for feature.
    /// </summary>
    public static class Feature
    {
        public const string Memory = "CrestApps.OrchardCore.AI.Memory";
    }

    /// <summary>
    /// Provides functionality for completion Context Keys.
    /// </summary>
    public static class CompletionContextKeys
    {
        public const string UserId = "AIMemoryUserId";

        /// <summary>
        /// The conversation's user and assistant messages older than the past-messages window, for
        /// <c>search_conversation</c>. Held on the invocation scope, so the tool only ever reads the
        /// conversation it is running in.
        /// </summary>
        public const string OlderConversationMessages = "AIMemoryOlderConversationMessages";

        /// <summary>
        /// The user, profile and current session that <c>search_past_conversations</c> and
        /// <c>list_recent_conversations</c> are scoped to.
        /// </summary>
        public const string PastConversationsScope = "AIMemoryPastConversationsScope";
    }

    /// <summary>
    /// Provides functionality for tool Purposes.
    /// </summary>
    public static class ToolPurposes
    {
        public const string UserMemory = "user_memory";
    }

    /// <summary>
    /// Provides functionality for column Names.
    /// </summary>
    public static class ColumnNames
    {
        public const string MemoryId = "memoryId";
        public const string UserId = "userId";
        public const string Name = "name";
        public const string Description = "description";
        public const string Content = "content";
        public const string UpdatedUtc = "updatedUtc";
        public const string Embedding = "embedding";
    }

    /// <summary>
    /// Provides functionality for template I Ds.
    /// </summary>
    public static class TemplateIds
    {
        public const string MemoryAvailability = "memory-availability";
        public const string MemoryContextHeader = "memory-context-header";
        public const string ConversationSummarization = "conversation-summarization";
        public const string ConversationMemoryContext = "conversation-memory-context";
        public const string PastConversationsAvailability = "past-conversations-availability";
    }
}
