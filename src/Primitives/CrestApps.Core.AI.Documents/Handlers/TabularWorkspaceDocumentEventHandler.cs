using CrestApps.Core.AI.Documents.Tabular;

namespace CrestApps.Core.AI.Documents.Handlers;

/// <summary>
/// Drops the in-memory tabular workspace when a tabular document is removed from a conversation,
/// freeing memory immediately and forcing a clean rebuild from the remaining documents on the
/// next request.
/// </summary>
internal sealed class TabularWorkspaceDocumentEventHandler : IAIChatDocumentEventHandler
{
    private readonly ITabularWorkspaceManager _workspaceManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="TabularWorkspaceDocumentEventHandler"/> class.
    /// </summary>
    /// <param name="workspaceManager">The tabular workspace manager.</param>
    public TabularWorkspaceDocumentEventHandler(ITabularWorkspaceManager workspaceManager)
    {
        _workspaceManager = workspaceManager;
    }

    /// <summary>
    /// Handles a completed document upload. No action is required because workspaces load lazily.
    /// </summary>
    /// <param name="context">The uploaded document context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task UploadedAsync(AIChatDocumentUploadContext context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles a completed document removal by dropping the associated tabular workspace.
    /// </summary>
    /// <param name="context">The removed document context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task RemovedAsync(AIChatDocumentRemoveContext context, CancellationToken cancellationToken = default)
    {
        if (context?.Document is null || !TabularFileTypes.IsTabular(context.Document.FileName))
        {
            return Task.CompletedTask;
        }

        var workspaceKey = ResolveWorkspaceKey(context);

        if (workspaceKey is not null)
        {
            _workspaceManager.RemoveConversation(workspaceKey);
        }

        return Task.CompletedTask;
    }

    private static string ResolveWorkspaceKey(AIChatDocumentRemoveContext context)
    {
        if (context.Interaction is not null && !string.IsNullOrEmpty(context.Interaction.ItemId))
        {
            return TabularWorkspaceKey.ForInteraction(context.Interaction.ItemId);
        }

        if (context.Session is not null && !string.IsNullOrEmpty(context.Session.SessionId))
        {
            return TabularWorkspaceKey.ForSession(context.Session.SessionId);
        }

        if (context.Profile is not null && !string.IsNullOrEmpty(context.Profile.ItemId))
        {
            return TabularWorkspaceKey.ForProfile(context.Profile.ItemId);
        }

        return null;
    }
}
