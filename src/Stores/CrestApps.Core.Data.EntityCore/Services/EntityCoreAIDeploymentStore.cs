using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Data.EntityCore.Services;

public sealed class EntityCoreAIDeploymentStore : NamedSourceDocumentCatalog<AIDeployment>
{
    public EntityCoreAIDeploymentStore(CrestAppsEntityDbContext dbContext)
        : base(dbContext)
    {
    }
}
