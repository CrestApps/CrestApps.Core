using CrestApps.Core.AI;
using CrestApps.Core.AI.Capabilities;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Connections;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task CreateRealtimeClientAsync_WhenMetadataDeclaredWithoutRealtime_Throws()
    {
        var factory = CreateFactory(out _);

        var deployment = CreateDeployment(AIModelFeatureNames.AudioInput);

        await Assert.ThrowsAsync<NotSupportedException>(
            async () => await factory.CreateRealtimeClientAsync(deployment));
    }

    [Fact]
    public async Task CreateRealtimeClientAsync_WhenRealtimeDeclared_PassesCapabilityCheck()
    {
        var factory = CreateFactory(out _);

        var deployment = CreateDeployment(AIModelFeatureNames.Realtime);

        // The capability check passes, so failure now comes from the (unresolved) connection rather than
        // from the realtime feature enforcement.
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await factory.CreateRealtimeClientAsync(deployment));
    }

    [Fact]
    public async Task CreateRealtimeClientAsync_WhenNoMetadata_IsUnconstrained()
    {
        var factory = CreateFactory(out _);

        var deployment = new AIDeployment
        {
            Name = "rt",
            ModelName = "gpt-realtime",
            ClientName = "OpenAI",
            ConnectionName = "conn",
        };

        // A deployment without capability metadata is never blocked by the realtime feature check; it
        // proceeds and fails later on the unresolved connection instead.
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await factory.CreateRealtimeClientAsync(deployment));
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

    private static DefaultAIClientFactory CreateFactory(out IServiceProvider serviceProvider)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IAIDeploymentStore>());
        services.AddCoreAIModelCapabilities();
        serviceProvider = services.BuildServiceProvider();

        return new DefaultAIClientFactory(
            clientProviders: [],
            connectionHandlers: [],
            dataProtectionProvider: Mock.Of<IDataProtectionProvider>(),
            serviceProvider: serviceProvider,
            connectionCatalog: Mock.Of<IAIProviderConnectionStore>());
    }
}
#pragma warning restore MEAI001
