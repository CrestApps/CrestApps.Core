using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Ingestion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Handlers;

/// <summary>
/// Deletes a conversation's presentation workspace — its working decks and their earlier versions — when the
/// conversation is deleted.
/// </summary>
internal sealed class PresentationWorkspaceCleanupHandler : IConversationWorkspaceCleanupHandler
{
    private readonly IDocumentFileStore _store;
    private readonly PresentationWorkspaceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PresentationWorkspaceCleanupHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PresentationWorkspaceCleanupHandler"/> class.
    /// </summary>
    /// <param name="store">The document file store.</param>
    /// <param name="options">The workspace options.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public PresentationWorkspaceCleanupHandler(
        IDocumentFileStore store,
        IOptions<PresentationWorkspaceOptions> options,
        TimeProvider timeProvider,
        ILogger<PresentationWorkspaceCleanupHandler> logger)
    {
        _store = store;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Deletes the workspace kept for the conversation.
    /// </summary>
    /// <param name="referenceId">The conversation identifier.</param>
    /// <param name="referenceType">The conversation reference type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task CleanupAsync(string referenceId, string referenceType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(referenceId) || string.IsNullOrEmpty(referenceType))
        {
            return Task.CompletedTask;
        }

        return PresentationWorkspaceMaintenance.RunAsync(_store, _options, _timeProvider, referenceType, referenceId, workspace => workspace.DeleteAllAsync(), _logger, cancellationToken);
    }
}
