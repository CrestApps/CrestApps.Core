using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Chat.Services;
using CrestApps.Core.AI.Models;
using Moq;

namespace CrestApps.Core.Tests.Framework.AI;

public sealed class DefaultAIChatSessionEventServiceTests
{
    [Fact]
    public async Task RecordSessionStartedAsync_SavesInitialAnalyticsRecord()
    {
        var capturedEvent = default(AIChatSessionEvent);
        var store = new Mock<IAIChatSessionEventStore>(MockBehavior.Strict);
        store
            .Setup(x => x.SaveAsync(It.IsAny<AIChatSessionEvent>(), TestContext.Current.CancellationToken))
            .Callback<AIChatSessionEvent, CancellationToken>((chatSessionEvent, _) => capturedEvent = chatSessionEvent)
            .Returns(Task.CompletedTask);

        var service = new DefaultAIChatSessionEventService(store.Object, TimeProvider.System);

        await service.RecordSessionStartedAsync(new AIChatSession
        {
            SessionId = "session-1",
            ProfileId = "profile-1",
            ClientId = "client-1",
        }, TestContext.Current.CancellationToken);

        Assert.NotNull(capturedEvent);
        Assert.Equal("session-1", capturedEvent.SessionId);
        Assert.Equal("profile-1", capturedEvent.ProfileId);
        Assert.Equal("client-1", capturedEvent.VisitorId);
        Assert.Equal(0, capturedEvent.MessageCount);
        store.VerifyAll();
    }

    [Fact]
    public async Task RecordCompletionUsageAsync_UpdatesExistingTokenTotals()
    {
        var chatSessionEvent = new AIChatSessionEvent
        {
            SessionId = "session-1",
            TotalInputTokens = 12,
            TotalOutputTokens = 8,
        };

        var store = new Mock<IAIChatSessionEventStore>(MockBehavior.Strict);
        store
            .Setup(x => x.FindBySessionIdAsync("session-1", TestContext.Current.CancellationToken))
            .ReturnsAsync(chatSessionEvent);
        store
            .Setup(x => x.SaveAsync(chatSessionEvent, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var service = new DefaultAIChatSessionEventService(store.Object, TimeProvider.System);

        await service.RecordCompletionUsageAsync("session-1", 5, 7, TestContext.Current.CancellationToken);

        Assert.Equal(17, chatSessionEvent.TotalInputTokens);
        Assert.Equal(15, chatSessionEvent.TotalOutputTokens);
        store.VerifyAll();
    }
}
