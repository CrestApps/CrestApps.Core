using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Data.EntityCore.Services;

public sealed class EntityCoreAIDataSourceStore : DocumentCatalog<AIDataSource>, IAIDataSourceStore
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EntityCoreAIDataSourceStore"/> class.
    /// </summary>
    /// <param name="dbContext">The db context.</param>
    /// <param name="logger">The logger.</param>
    public EntityCoreAIDataSourceStore(
        CrestAppsEntityDbContext dbContext,
        ILogger<DocumentCatalog<AIDataSource>> logger = null)
        : base(dbContext, logger)
    {
    }
}
