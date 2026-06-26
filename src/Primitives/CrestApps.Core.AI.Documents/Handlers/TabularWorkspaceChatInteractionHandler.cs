using CrestApps.Core.AI.Documents.Tabular;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Handlers;
using CrestApps.Core.Models;

namespace CrestApps.Core.AI.Documents.Handlers;

internal sealed class TabularWorkspaceChatInteractionHandler : CatalogEntryHandlerBase<ChatInteraction>
{
    private readonly IEnumerable<ITabularWorkspaceInvalidationPublisher> _invalidationPublishers;

    public TabularWorkspaceChatInteractionHandler(IEnumerable<ITabularWorkspaceInvalidationPublisher> invalidationPublishers)
    {
        _invalidationPublishers = invalidationPublishers;
    }

    public override async Task DeletedAsync(DeletedContext<ChatInteraction> context, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(context.Model?.ItemId))
        {
            var invalidation = TabularWorkspaceInvalidation.ForChatInteraction(context.Model.ItemId);
            await _invalidationPublishers.PublishAllAsync(invalidation, cancellationToken);
        }
    }
}
