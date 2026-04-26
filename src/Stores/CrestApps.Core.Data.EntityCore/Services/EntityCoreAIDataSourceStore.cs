using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Data.EntityCore.Services;

public sealed class EntityCoreAIDataSourceStore : DocumentCatalog<AIDataSource>, IAIDataSourceStore
{
    public EntityCoreAIDataSourceStore(
        CrestAppsEntityDbContext dbContext,
        ILogger<DocumentCatalog<AIDataSource>> logger = null)
        : base(dbContext, logger)
    {
    }
}
