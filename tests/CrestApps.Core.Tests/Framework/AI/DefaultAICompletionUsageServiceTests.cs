using System.Security.Claims;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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
            httpContextAccessor.Object);

        await service.UsageRecordedAsync(record, TestContext.Current.CancellationToken);

        Assert.Equal("alice", record.UserName);
        Assert.NotEqual(default, record.CreatedUtc);
        usageStore.VerifyAll();
        chatSessionEventService.VerifyAll();
        serviceProvider.VerifyAll();
    }

    [Fact]
    public void AddCoreAIUsageTracking_RegistersTheService()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(Mock.Of<IAICompletionUsageStore>())
            .AddSingleton(Mock.Of<IHttpContextAccessor>())
            .AddSingleton(TimeProvider.System)
            .AddCoreAIUsageTracking();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<DefaultAICompletionUsageService>(scope.ServiceProvider.GetRequiredService<IAICompletionUsageService>());
    }

    [Fact]
    public void WithoutAddCoreAIUsageTracking_NothingIsRegistered()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();

        Assert.Null(provider.GetService<IAICompletionUsageService>());
    }
}
