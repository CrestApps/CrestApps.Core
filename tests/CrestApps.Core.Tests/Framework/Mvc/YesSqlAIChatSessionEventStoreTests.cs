using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql;
using CrestApps.Core.Data.YesSql.Services;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class YesSqlAIChatSessionEventStoreTests
{
    [Fact]
    public async Task SaveAsync_WritesAnalyticsRecordToAiCollection()
    {
        var session = new Mock<global::YesSql.ISession>(MockBehavior.Strict);
        session
            .Setup(store => store.SaveAsync(It.IsAny<AIChatSessionEvent>(), false, "AI", TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var store = new YesSqlAIChatSessionEventStore(
            session.Object,
            Options.Create(new YesSqlStoreOptions()));

        await store.SaveAsync(new AIChatSessionEvent
        {
            SessionId = "session-1",
        }, TestContext.Current.CancellationToken);

        session.VerifyAll();
    }
}
