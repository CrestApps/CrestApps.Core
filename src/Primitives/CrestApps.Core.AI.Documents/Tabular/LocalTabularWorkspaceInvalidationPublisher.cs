namespace CrestApps.Core.AI.Documents.Tabular;

internal sealed class LocalTabularWorkspaceInvalidationPublisher : ITabularWorkspaceInvalidationPublisher
{
    private readonly ITabularWorkspaceInvalidator _workspaceInvalidator;

    public LocalTabularWorkspaceInvalidationPublisher(ITabularWorkspaceInvalidator workspaceInvalidator)
    {
        _workspaceInvalidator = workspaceInvalidator;
    }

    public Task PublishAsync(
        TabularWorkspaceInvalidation invalidation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invalidation);
        cancellationToken.ThrowIfCancellationRequested();

        switch (invalidation.Kind)
        {
            case TabularWorkspaceInvalidation.ReferenceKind:
                _workspaceInvalidator.InvalidateReference(invalidation.ReferenceType, invalidation.ReferenceId);
                break;

            case TabularWorkspaceInvalidation.ChatInteractionKind:
                _workspaceInvalidator.InvalidateChatInteraction(invalidation.ReferenceId);
                break;

            case TabularWorkspaceInvalidation.ChatSessionKind:
                _workspaceInvalidator.InvalidateChatSession(invalidation.ReferenceId);
                break;

            case TabularWorkspaceInvalidation.ProfileKind:
                _workspaceInvalidator.InvalidateProfile(invalidation.ReferenceId);
                break;
        }

        return Task.CompletedTask;
    }
}
