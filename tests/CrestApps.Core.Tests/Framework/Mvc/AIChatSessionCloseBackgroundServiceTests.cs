using System.Reflection;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Chat.Services;
using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrestApps.Core.Tests.Framework.Mvc;

public sealed class AIChatSessionCloseBackgroundServiceTests
{
    [Fact]
    public void AddCoreAIChatSessionProcessing_RegistersHostedService()
    {
        var services = new ServiceCollection();

        services.AddCoreAIChatSessionProcessing();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(AIChatSessionCloseBackgroundService)
            && descriptor.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void DetermineInactiveSessionStatus_WithUserPrompt_ReturnsClosed()
    {
        var status = DetermineInactiveSessionStatus(
        [
            new AIChatSessionPrompt
            {
                Role = ChatRole.User,
                Content = "I need help.",
            },
        ]);

        Assert.Equal(ChatSessionStatus.Closed, status);
    }

    [Fact]
    public void DetermineInactiveSessionStatus_WithoutUserPrompt_ReturnsAbandoned()
    {
        var status = DetermineInactiveSessionStatus(
        [
            new AIChatSessionPrompt
            {
                Role = ChatRole.Assistant,
                Content = "Hello, how can I help?",
            },
        ]);

        Assert.Equal(ChatSessionStatus.Abandoned, status);
    }

    private static ChatSessionStatus DetermineInactiveSessionStatus(IReadOnlyList<AIChatSessionPrompt> prompts)
    {
        var method = typeof(AIChatSessionCloseBackgroundService).GetMethod(
            "DetermineInactiveSessionStatus",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        return (ChatSessionStatus)method.Invoke(null, new object[] { prompts });
    }
}
