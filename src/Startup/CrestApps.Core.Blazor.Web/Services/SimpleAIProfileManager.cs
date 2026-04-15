using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Services;

namespace CrestApps.Core.Blazor.Web.Services;

public sealed class SimpleAIProfileManager : NamedCatalogManager<AIProfile>, IAIProfileManager
{
    public SimpleAIProfileManager(
        INamedCatalog<AIProfile> catalog,
        IEnumerable<ICatalogEntryHandler<AIProfile>> handlers,
        ILogger<SimpleAIProfileManager> logger)
        : base(catalog, handlers, logger)
    {
    }

    public async ValueTask<IEnumerable<AIProfile>> GetAsync(AIProfileType type)
    {
        var all = await GetAllAsync();

        return all.Where(p => p.Type == type);
    }
}
