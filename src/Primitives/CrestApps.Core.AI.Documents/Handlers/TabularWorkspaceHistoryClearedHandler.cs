using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Documents.Handlers;

/// <summary>
/// Deletes the file-backed tabular workspace database when a chat interaction's history is cleared,
/// so the next tool call reloads fresh data from the original uploaded files instead of retaining
/// mutations from the previous conversation.
/// </summary>
internal sealed class TabularWorkspaceHistoryClearedHandler : IChatInteractionHistoryHandler
{
    private readonly string _basePath;
    private readonly ILogger<TabularWorkspaceHistoryClearedHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TabularWorkspaceHistoryClearedHandler"/> class.
    /// </summary>
    /// <param name="fileStoreOptions">The document file store options.</param>
    /// <param name="logger">The logger.</param>
    public TabularWorkspaceHistoryClearedHandler(
        IOptions<DocumentFileSystemFileStoreOptions> fileStoreOptions,
        ILogger<TabularWorkspaceHistoryClearedHandler> logger)
    {
        _basePath = fileStoreOptions.Value.BasePath;
        _logger = logger;
    }

    /// <summary>
    /// Deletes the workspace database so the next request starts fresh from the original files.
    /// </summary>
    /// <param name="interaction">The chat interaction whose history was cleared.</param>
    /// <param name="clearedPrompts">The prompts (messages) that were removed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task HistoryClearedAsync(
        ChatInteraction interaction,
        IReadOnlyCollection<ChatInteractionPrompt> clearedPrompts,
        CancellationToken cancellationToken = default)
    {
        if (interaction is null || string.IsNullOrEmpty(interaction.ItemId) || string.IsNullOrEmpty(_basePath))
        {
            return Task.CompletedTask;
        }

        var databasePath = Path.Combine(_basePath, "documents", AIReferenceTypes.Document.ChatInteraction, interaction.ItemId, "data", "tabular.db");
        TryDeleteFile(databasePath);
        TryDeleteFile(databasePath + "-wal");
        TryDeleteFile(databasePath + "-shm");

        return Task.CompletedTask;
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(ex, "Failed to delete tabular database file '{Path}'.", path);
            }
        }
    }
}
