using System.Security.Claims;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Framework.AI;

public sealed class DefaultAICompletionUsageServiceTests
{
    [Fact]
    public async Task UsageRecordedAsync_SavesUsageAndForwardsSessionTokenTotals()
    {
        var record = new AICompletionUsageRecord
        {
            SessionId = "session-1",
            InputTokenCount = 4,
            OutputTokenCount = 9,
        };

        var usageStore = new Mock<IAICompletionUsageStore>(MockBehavior.Strict);
        usageStore
            .Setup(x => x.SaveAsync(record, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var chatSessionEventService = new Mock<IAIChatSessionEventService>(MockBehavior.Strict);
        chatSessionEventService
            .Setup(x => x.RecordCompletionUsageAsync("session-1", 4, 9, TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        var serviceProvider = new Mock<IServiceProvider>(MockBehavior.Strict);
        serviceProvider
            .Setup(x => x.GetService(typeof(IAIChatSessionEventService)))
            .Returns(chatSessionEventService.Object);

        var httpContextAccessor = new Mock<IHttpContextAccessor>(MockBehavior.Strict);
        httpContextAccessor.SetupGet(x => x.HttpContext).Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], "test")),
        });

        var service = new DefaultAICompletionUsageService(
            usageStore.Object,
            serviceProvider.Object,
            TimeProvider.System,
            httpContextAccessor.Object,
            Options.Create(new GeneralAIOptions { EnableAIUsageTracking = true }));

        await service.UsageRecordedAsync(record, TestContext.Current.CancellationToken);

        Assert.Equal("alice", record.UserName);
        Assert.NotEqual(default, record.CreatedUtc);
        usageStore.VerifyAll();
        chatSessionEventService.VerifyAll();
        serviceProvider.VerifyAll();
    }

    [Fact]
    public async Task UsageRecordedAsync_DoesNotSaveWhenTrackingIsDisabled()
    {
        var usageStore = new Mock<IAICompletionUsageStore>(MockBehavior.Strict);
        var serviceProvider = new Mock<IServiceProvider>(MockBehavior.Strict);
        var httpContextAccessor = new Mock<IHttpContextAccessor>(MockBehavior.Strict);

        var service = new DefaultAICompletionUsageService(
            usageStore.Object,
            serviceProvider.Object,
            TimeProvider.System,
            httpContextAccessor.Object,
            Options.Create(new GeneralAIOptions { EnableAIUsageTracking = false }));

        await service.UsageRecordedAsync(new AICompletionUsageRecord(), TestContext.Current.CancellationToken);
    }
}
