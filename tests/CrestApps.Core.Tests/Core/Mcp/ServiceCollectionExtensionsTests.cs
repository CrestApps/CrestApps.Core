using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.OrchardCore.Tests.Core.Mcp;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCoreAIMcpServices_RegistersSharedClientServicesAndSseTransport()
    {
        var services = new ServiceCollection();

        services.AddCoreAIMcpServices();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IOAuth2TokenService) &&
            descriptor.ImplementationType == typeof(DefaultOAuth2TokenService) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IMcpMetadataPromptGenerator) &&
            descriptor.ImplementationType == typeof(DefaultMcpMetadataPromptGenerator) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IMcpCapabilityEmbeddingCacheProvider) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IMcpServerMetadataCacheProvider) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IMcpCapabilityResolver) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IMcpClientTransportProvider) &&
            descriptor.ImplementationType == typeof(SseClientTransportProvider));

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<McpClientAIOptions>>().Value;

        Assert.Contains(McpConstants.TransportTypes.Sse, options.TransportTypes.Keys);
        Assert.DoesNotContain(McpConstants.TransportTypes.StdIo, options.TransportTypes.Keys);
    }

    [Fact]
    public void AddCoreAIMcpClient_WhenStdIoDisabled_DoesNotRegisterStdIoTransport()
    {
        var services = new ServiceCollection();

        services.AddCoreAIMcpClient(includeStdIoTransport: false);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IAICompletionContextBuilderHandler) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IToolRegistryProvider) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IMcpClientTransportProvider) &&
            descriptor.ImplementationType == typeof(StdioClientTransportProvider));

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<McpClientAIOptions>>().Value;

        Assert.Contains(McpConstants.TransportTypes.Sse, options.TransportTypes.Keys);
        Assert.DoesNotContain(McpConstants.TransportTypes.StdIo, options.TransportTypes.Keys);
    }

    [Fact]
    public void AddCoreAIMcpClient_WhenStdIoEnabled_RegistersStdIoTransport()
    {
        var services = new ServiceCollection();

        services.AddCoreAIMcpClient();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IMcpClientTransportProvider) &&
            descriptor.ImplementationType == typeof(StdioClientTransportProvider));

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<McpClientAIOptions>>().Value;

        Assert.Contains(McpConstants.TransportTypes.Sse, options.TransportTypes.Keys);
        Assert.Contains(McpConstants.TransportTypes.StdIo, options.TransportTypes.Keys);
    }

    [Fact]
    public void AddCoreAIMcpServer_RegistersSharedServerServices()
    {
        var services = new ServiceCollection();

        services.AddCoreAIMcpServer();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IMcpServerPromptService) &&
            descriptor.ImplementationType == typeof(DefaultMcpServerPromptService) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IMcpServerResourceService) &&
            descriptor.ImplementationType == typeof(DefaultMcpServerResourceService) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);
    }
}
