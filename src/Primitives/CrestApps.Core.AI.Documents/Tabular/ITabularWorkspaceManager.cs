namespace CrestApps.Core.AI.Documents.Tabular;

/// <summary>
/// Manages in-memory tabular data workspaces for AI conversations. Uploaded CSV/TSV/Excel files
/// are loaded lazily into an in-memory SQLite database so the model can query and manipulate the
/// data with SQL instead of loading raw rows into the prompt.
/// <para>
/// The in-memory database is <b>request-scoped</b>: it is built on first use within a request,
/// reused across every tool call in that same request (so the agent never creates duplicate
/// tables for the same file in one cycle), and disposed when the request/prompt completes. Asking
/// about the file again in a later request rebuilds a fresh in-memory database. A lightweight,
/// replayable manipulation journal is retained per conversation so previously applied changes are
/// reapplied when the workspace is rebuilt, preserving the latest state without keeping the heavy
/// database in memory between prompts.
/// </para>
/// </summary>
public interface ITabularWorkspaceManager
{
    /// <summary>
    /// Ensures the request's in-memory database is built and synchronized with the supplied
    /// tabular documents, rebuilding it (and replaying recorded manipulations) when this request
    /// has no live database yet. Loading is lazy: document content is only requested through
    /// <paramref name="contentLoader"/> when a table actually needs to be created. Calling this
    /// multiple times within the same request (same <paramref name="requestId"/>) reuses the
    /// already-built database rather than creating new tables.
    /// </summary>
    /// <param name="conversationKey">The stable conversation key (chat session/interaction/profile).</param>
    /// <param name="requestId">The unique identifier of the current request/prompt.</param>
    /// <param name="documents">The tabular documents that should be available in the workspace.</param>
    /// <param name="contentLoader">A delegate that loads the raw tabular content for a document id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tables available in the workspace after synchronization.</returns>
    Task<IReadOnlyList<TabularTableInfo>> EnsureReadyAsync(
        string conversationKey,
        string requestId,
        IReadOnlyList<TabularDocumentRef> documents,
        Func<string, CancellationToken, Task<string>> contentLoader,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the tables currently loaded for the conversation, including their schema and row
    /// counts. Returns an empty list when no request currently has a live in-memory database.
    /// </summary>
    /// <param name="conversationKey">The conversation key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The loaded tables, or an empty list when nothing is loaded.</returns>
    Task<IReadOnlyList<TabularTableInfo>> GetTablesAsync(string conversationKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a read-only SQL query (a single <c>SELECT</c> or <c>WITH … SELECT</c> statement)
    /// against the conversation's live in-memory database and returns up to
    /// <paramref name="maxRows"/> rows.
    /// </summary>
    /// <param name="conversationKey">The conversation key.</param>
    /// <param name="sql">The read-only SQL query.</param>
    /// <param name="maxRows">The maximum number of rows to return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The query result.</returns>
    Task<TabularQueryResult> QueryAsync(string conversationKey, string sql, int maxRows, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a single data-manipulation or schema statement against the conversation's live
    /// in-memory database and records it in the rebuild journal so the change is replayed when the
    /// workspace is rebuilt in a later request.
    /// </summary>
    /// <param name="conversationKey">The conversation key.</param>
    /// <param name="sql">The manipulation or schema statement.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The command result.</returns>
    Task<TabularCommandResult> ExecuteAsync(string conversationKey, string sql, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases the current request's hold on the conversation workspace. When no other request is
    /// still using it, the in-memory database is disposed immediately to free memory while the
    /// rebuild journal is retained. Called when the request/prompt completes.
    /// </summary>
    /// <param name="conversationKey">The conversation key.</param>
    /// <param name="requestId">The unique identifier of the completing request/prompt.</param>
    void ReleaseRequest(string conversationKey, string requestId);

    /// <summary>
    /// Removes a conversation workspace entirely, disposing any live in-memory database and
    /// discarding its rebuild journal. Called when the underlying documents are removed.
    /// </summary>
    /// <param name="conversationKey">The conversation key.</param>
    void RemoveConversation(string conversationKey);
}
