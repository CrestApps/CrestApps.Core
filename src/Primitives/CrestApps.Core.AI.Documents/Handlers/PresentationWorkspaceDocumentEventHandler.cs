using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Ingestion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Handlers;

/// <summary>
/// Removes the working copy of an uploaded deck when the upload is removed from the conversation, so the
/// agent stops offering a deck the user took away.
/// </summary>
internal sealed class PresentationWorkspaceDocumentEventHandler : IAIChatDocumentEventHandler
{
    private readonly IDocumentFileStore _store;
    private readonly PresentationWorkspaceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PresentationWorkspaceDocumentEventHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PresentationWorkspaceDocumentEventHandler"/> class.
    /// </summary>
    /// <param name="store">The document file store.</param>
    /// <param name="options">The workspace options.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public PresentationWorkspaceDocumentEventHandler(
        IDocumentFileStore store,
        IOptions<PresentationWorkspaceOptions> options,
        TimeProvider timeProvider,
        ILogger<PresentationWorkspaceDocumentEventHandler> logger)
    {
        _store = store;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Does nothing: an upload is copied into the workspace the first time a presentation tool runs.
    /// </summary>
    /// <param name="context">The upload context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task UploadedAsync(AIChatDocumentUploadContext context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes the deck imported from the removed upload.
    /// </summary>
    /// <param name="context">The removal context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task RemovedAsync(AIChatDocumentRemoveContext context, CancellationToken cancellationToken = default)
    {
        if (context?.DocumentInfo is not { } document || !_options.IsPresentationFile(document.FileName))
        {
            return Task.CompletedTask;
        }

        return PresentationWorkspaceMaintenance.RunAsync(_store, _options, _timeProvider, context.ReferenceType, context.ReferenceId, workspace => workspace.RemoveImportedAsync(document.DocumentId), _logger, cancellationToken);
    }
}
