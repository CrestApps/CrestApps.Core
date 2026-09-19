using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.AIChat;

/// <summary>
/// YesSql index provider that maps <see cref="AIChatSession"/> documents
/// to <see cref="AIChatSessionIndex"/> entries in the AI collection.
/// </summary>
public sealed class AIChatSessionIndexProvider : IndexProvider<AIChatSession>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIChatSessionIndexProvider"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public AIChatSessionIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    /// <summary>
    /// Describes the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    public override void Describe(DescribeContext<AIChatSession> context)
    {
        context.For<AIChatSessionIndex>()
            .Map(session => new AIChatSessionIndex
            {
                SessionId = session.SessionId,
                ProfileId = session.ProfileId,
                UserId = session.UserId,
                Status = session.Status,
                LastActivityUtc = session.LastActivityUtc,
            });
    }
}
