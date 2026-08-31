using CrestApps.Core.AI;
using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Connections;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Framework.AI;

#pragma warning disable MEAI001 // The realtime API from Microsoft.Extensions.AI is for evaluation purposes only.
public sealed class AIRealtimeCapabilityTests
{
    [Fact]
    public void AddCoreAIModelCapabilities_RegistersRealtimeFeature()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IAIDeploymentStore>());
        services.AddCoreAIModelCapabilities();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AIModelCapabilityOptions>>().Value;

        Assert.True(options.Features.ContainsKey(AIModelFeatureNames.Realtime));
    }

    [Fact]
    public async Task CreateRealtimeClientAsync_WhenMetadataDeclaredWithoutRealtime_WarnsButDoesNotBlock()
    {
        var factory = CreateFactory(out var logger);

        var deployment = CreateDeployment(AIModelFeatureNames.AudioInput);

        // The realtime feature is not declared, so a warning is logged but the request is not blocked; it
        // proceeds and fails later on the unresolved connection instead of throwing NotSupportedException.
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await factory.CreateRealtimeClientAsync(deployment));

        VerifyWarning(logger, Times.Once());
    }

    [Fact]
    public async Task CreateRealtimeClientAsync_WhenRealtimeDeclared_DoesNotWarn()
    {
        var factory = CreateFactory(out var logger);

        var deployment = CreateDeployment(AIModelFeatureNames.Realtime);

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await factory.CreateRealtimeClientAsync(deployment));

        VerifyWarning(logger, Times.Never());
    }

    [Fact]
    public async Task CreateRealtimeClientAsync_WhenNoMetadata_DoesNotWarn()
    {
        var factory = CreateFactory(out var logger);

        var deployment = new AIDeployment
        {
            Name = "rt",
            ModelName = "gpt-realtime",
            ClientName = "OpenAI",
            ConnectionName = "conn",
        };

        // A deployment without capability metadata is unconstrained: it produces no warning and proceeds,
        // failing later on the unresolved connection.
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await factory.CreateRealtimeClientAsync(deployment));

        VerifyWarning(logger, Times.Never());
    }

    private static AIDeployment CreateDeployment(params string[] features)
    {
        var deployment = new AIDeployment
        {
            Name = "rt",
            ModelName = "gpt-realtime",
            ClientName = "OpenAI",
            ConnectionName = "conn",
        };

        deployment.Put(new AIDeploymentModelMetadata
        {
            Features = features,
        });

        return deployment;
    }

    private static DefaultAIClientFactory CreateFactory(out Mock<ILogger<DefaultAIClientFactory>> logger)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IAIDeploymentStore>());

        logger = new Mock<ILogger<DefaultAIClientFactory>>();
        services.AddSingleton(logger.Object);

        services.AddCoreAIModelCapabilities();
        var serviceProvider = services.BuildServiceProvider();

        return new DefaultAIClientFactory(
            clientProviders: [],
            connectionHandlers: [],
            dataProtectionProvider: Mock.Of<IDataProtectionProvider>(),
            serviceProvider: serviceProvider,
            connectionCatalog: Mock.Of<IAIProviderConnectionStore>());
    }

    private static void VerifyWarning(Mock<ILogger<DefaultAIClientFactory>> logger, Times times)
    {
        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            times);
    }
}
#pragma warning restore MEAI001
