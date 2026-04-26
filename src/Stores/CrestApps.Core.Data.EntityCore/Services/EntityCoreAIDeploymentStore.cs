using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Data.EntityCore.Services;

public sealed class EntityCoreAIDeploymentStore : NamedSourceDocumentCatalog<AIDeployment>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EntityCoreAIDeploymentStore"/> class.
    /// </summary>
    /// <param name="dbContext">The db context.</param>
    public EntityCoreAIDeploymentStore(CrestAppsEntityDbContext dbContext)
        : base(dbContext)
    {
    }
}
