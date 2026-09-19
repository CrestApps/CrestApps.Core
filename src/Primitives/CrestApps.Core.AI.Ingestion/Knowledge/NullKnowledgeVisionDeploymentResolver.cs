namespace CrestApps.Core.AI.Ingestion.Knowledge;

/// <summary>
/// Answers nothing, so the host's vision deployment is used.
/// </summary>
internal sealed class NullKnowledgeVisionDeploymentResolver : IKnowledgeVisionDeploymentResolver
{
    /// <inheritdoc />
    public Task<string> ResolveAsync(string fileSourceId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string>(null);
    }
}
