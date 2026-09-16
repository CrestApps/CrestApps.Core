using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace CrestApps.Core.AI.Documents;

/// <summary>
/// Authorization operations for ingested knowledge.
/// </summary>
public static class AIKnowledgeOperations
{
    /// <summary>
    /// Gets the requirement checked before a stored figure is served. The resource passed to the
    /// authorization service is the owning <see cref="CrestApps.Core.AI.Models.AIDataSource"/>.
    /// </summary>
    public static OperationAuthorizationRequirement ViewFigures { get; } = new()
    {
        Name = nameof(ViewFigures),
    };
}
