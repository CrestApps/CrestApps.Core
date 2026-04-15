using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Options;
using YesSql.Indexes;

namespace CrestApps.Core.Data.YesSql.Indexes.AIChat;

public sealed class AIChatSessionIndex : MapIndex
{
    public string SessionId { get; set; }

    public string ProfileId { get; set; }

    public string UserId { get; set; }

    public int Status { get; set; }

    public DateTime LastActivityUtc { get; set; }
}

public sealed class AIChatSessionIndexProvider : IndexProvider<AIChatSession>
{
    public AIChatSessionIndexProvider(IOptions<YesSqlStoreOptions> options)
    {
        CollectionName = options.Value.AICollectionName;
    }

    public override void Describe(DescribeContext<AIChatSession> context)
    {
        context.For<AIChatSessionIndex>()
            .Map(session => new AIChatSessionIndex
            {
                SessionId = session.SessionId,
                ProfileId = session.ProfileId,
                UserId = session.UserId,
                Status = (int)session.Status,
                LastActivityUtc = session.LastActivityUtc,
            });
    }
}
