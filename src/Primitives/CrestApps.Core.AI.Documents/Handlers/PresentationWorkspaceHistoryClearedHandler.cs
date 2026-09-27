using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Documents.Presentations;
using CrestApps.Core.AI.Ingestion;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Handlers;

/// <summary>
/// Deletes a chat interaction's presentation workspace when its history is cleared, so the next request
/// starts again from the uploaded decks rather than from edits the cleared conversation made.
/// </summary>
internal sealed class PresentationWorkspaceHistoryClearedHandler : IChatInteractionHistoryHandler
{
    private readonly IDocumentFileStore _store;
    private readonly PresentationWorkspaceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PresentationWorkspaceHistoryClearedHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PresentationWorkspaceHistoryClearedHandler"/> class.
    /// </summary>
    /// <param name="store">The document file store.</param>
    /// <param name="options">The workspace options.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="logger">The logger.</param>
    public PresentationWorkspaceHistoryClearedHandler(
        IDocumentFileStore store,
        IOptions<PresentationWorkspaceOptions> options,
        TimeProvider timeProvider,
        ILogger<PresentationWorkspaceHistoryClearedHandler> logger)
    {
        _store = store;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Deletes the workspace.
    /// </summary>
    /// <param name="interaction">The chat interaction whose history was cleared.</param>
    /// <param name="clearedPrompts">The prompts that were removed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task HistoryClearedAsync(ChatInteraction interaction, IReadOnlyCollection<ChatInteractionPrompt> clearedPrompts, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(interaction?.ItemId))
        {
            return Task.CompletedTask;
        }

        return PresentationWorkspaceMaintenance.RunAsync(_store, _options, _timeProvider, AIReferenceTypes.Document.ChatInteraction, interaction.ItemId, workspace => workspace.DeleteAllAsync(), _logger, cancellationToken);
    }
}
