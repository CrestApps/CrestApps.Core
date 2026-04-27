using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Data.YesSql.Indexes.AI;
using Microsoft.Extensions.Options;
using YesSql;

namespace CrestApps.Core.Data.YesSql.Services;

/// <summary>
/// YesSql-backed store for AI profiles.
/// </summary>
public sealed class YesSqlAIProfileStore : NamedSourceDocumentCatalog<AIProfile, AIProfileIndex>, IAIProfileStore
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YesSqlAIProfileStore"/> class.
    /// </summary>
    /// <param name="session">The YesSql session.</param>
    /// <param name="options">The store options.</param>
    public YesSqlAIProfileStore(
        ISession session,
        IOptions<YesSqlStoreOptions> options)
        : base(session, options.Value.AICollectionName)
    {
    }

    /// <summary>
    /// Gets AI profiles by profile type.
    /// </summary>
    /// <param name="type">The profile type.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<IReadOnlyCollection<AIProfile>> GetByTypeAsync(AIProfileType type, CancellationToken cancellationToken = default)
    {
        var profileType = type.ToString();
        var items = await Session.Query<AIProfile, AIProfileIndex>(x => x.Type == profileType, collection: CollectionName)
            .ListAsync(cancellationToken);

        return items.ToArray();
    }
}
