namespace CrestApps.Core.Infrastructure.Indexing;

/// <summary>
/// Handles successful document mutations performed through <see cref="ISearchDocumentManager"/>.
/// Implement this contract when you need to react after a source index successfully writes or removes
/// documents and keep follow-up work asynchronous without modifying the provider-specific document manager.
/// </summary>
public interface ISearchDocumentHandler
{
    /// <summary>
    /// Called after <see cref="ISearchDocumentManager"/> successfully adds or updates documents in a source index.
    /// </summary>
    /// <param name="profile">The source index profile that completed the write operation.</param>
    /// <param name="documentIds">The normalized set of document ids that were written successfully.</param>
    /// <param name="cancellationToken">A token that cancels any follow-up asynchronous work.</param>
    /// <returns>A task that completes when the handler has finished processing the notification.</returns>
    Task DocumentsAddedOrUpdatedAsync(
        IIndexProfileInfo profile,
        IReadOnlyCollection<string> documentIds,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>
    /// Called after <see cref="ISearchDocumentManager"/> successfully deletes documents from a source index.
    /// </summary>
    /// <param name="profile">The source index profile that completed the delete operation.</param>
    /// <param name="documentIds">The normalized set of document ids that were deleted successfully.</param>
    /// <param name="cancellationToken">A token that cancels any follow-up asynchronous work.</param>
    /// <returns>A task that completes when the handler has finished processing the notification.</returns>
    Task DocumentsDeletedAsync(
        IIndexProfileInfo profile,
        IReadOnlyCollection<string> documentIds,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
