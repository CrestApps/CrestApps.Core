using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql;
using CrestApps.Core.Data.YesSql.Services;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class YesSqlAICompletionUsageStoreTests
{
    [Fact]
    public async Task SaveAsync_WritesUsageRecordToAiCollection()
    {
        var session = new Mock<global::YesSql.ISession>(MockBehavior.Strict);
        session
            .Setup(store => store.SaveAsync(It.IsAny<AICompletionUsageRecord>(), false, "AI", TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var store = new YesSqlAICompletionUsageStore(
            session.Object,
            Options.Create(new YesSqlStoreOptions()));

        await store.SaveAsync(new AICompletionUsageRecord(), TestContext.Current.CancellationToken);

        session.VerifyAll();
    }
}
