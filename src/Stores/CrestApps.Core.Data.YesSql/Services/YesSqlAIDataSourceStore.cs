using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql.Indexes.DataSources;
using Microsoft.Extensions.Options;
using YesSql;

namespace CrestApps.Core.Data.YesSql.Services;

public sealed class YesSqlAIDataSourceStore : DocumentCatalog<AIDataSource, AIDataSourceIndex>, IAIDataSourceStore
{
    public YesSqlAIDataSourceStore(
        ISession session,
        IOptions<YesSqlStoreOptions> options)
        : base(session, options.Value.AIDocsCollectionName)
    {
    }
}
