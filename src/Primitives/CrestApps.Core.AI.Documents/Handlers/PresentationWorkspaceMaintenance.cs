using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Ingestion;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Documents.Handlers;

/// <summary>
/// Opens a conversation's presentation workspace under its lock for housekeeping, and never lets a failure
/// to tidy up fail the operation that asked for it.
/// </summary>
internal static class PresentationWorkspaceMaintenance
{
    /// <summary>
    /// Runs an action on a conversation's workspace, if it has one.
    /// </summary>
    /// <param name="store">The document file store.</param>
    /// <param name="options">The workspace options.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="referenceType">The conversation's reference type.</param>
    /// <param name="referenceId">The conversation's identifier.</param>
    /// <param name="action">What to do with the workspace.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the action has run.</returns>
    public static async Task RunAsync(
        IDocumentFileStore store,
        PresentationWorkspaceOptions options,
        TimeProvider timeProvider,
        string referenceType,
        string referenceId,
        Func<PresentationWorkspace, Task> action,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var folder = PresentationWorkspaceStorage.GetFolder(referenceType, referenceId);

        if (folder is null)
        {
            return;
        }

        try
        {
            using var handle = await PresentationWorkspace.AcquireAsync(folder, cancellationToken);
            var workspace = await PresentationWorkspace.LoadAsync(store, folder, options, timeProvider);

            if (workspace.State.Decks.Count == 0 && workspace.State.DismissedDocumentIds.Count == 0)
            {
                return;
            }

            await action(workspace);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The presentation workspace of '{ReferenceId}' could not be cleaned up.", referenceId);
        }
    }
}
