using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql.Indexes.AI;
using CrestApps.Core.Data.YesSql.Services;
using ISession = YesSql.ISession;

namespace CrestApps.Core.Mvc.Web.Areas.AI.Services;

public sealed class YesSqlAIDeploymentStore : NamedSourceDocumentCatalog<AIDeployment, AIDeploymentIndex>
{
    public YesSqlAIDeploymentStore(ISession session)
        : base(session)
    {
    }
}
