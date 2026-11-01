using CrestApps.Core.AI.Tooling;
using CrestApps.Core.AI.WebMcp;
using CrestApps.Core.AI.WebMcp.Services;
using CrestApps.Core.Builders;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Tests.Core.WebMcp;

public sealed class WebMcpServiceCollectionExtensionsTests
{
    [Fact]
    public void AddWebMcp_RegistersTheToolServiceAndTheDefaultAccessEvaluator()
    {
        var services = new ServiceCollection();

        new CrestAppsAISuiteBuilder(services).AddWebMcp();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWebMcpToolService) && descriptor.ImplementationType == typeof(DefaultWebMcpToolService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWebMcpAccessEvaluator) && descriptor.ImplementationType == typeof(DefaultWebMcpAccessEvaluator));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IWebMcpPageToolRegistry));
    }

    [Fact]
    public void AddWebMcp_KeepsAnAccessEvaluatorTheHostRegisteredFirst()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IWebMcpAccessEvaluator, AdminOnlyAccessEvaluator>();

        new CrestAppsAISuiteBuilder(services).AddWebMcp();

        var descriptor = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IWebMcpAccessEvaluator));

        Assert.Equal(typeof(AdminOnlyAccessEvaluator), descriptor.ImplementationType);
    }

    [Fact]
    public void AddPageTools_RegistersTheRegistryTheHubClientAndTheToolProviderOnce()
    {
        var services = new ServiceCollection();

        new CrestAppsAISuiteBuilder(services).AddWebMcp(webMcp => webMcp
            .AddPageTools<TestHub>()
            .AddPageTools<TestHub>());

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IWebMcpPageToolRegistry));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IWebMcpPageToolClient) && descriptor.ImplementationType == typeof(HubWebMcpPageToolClient<TestHub>));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IToolRegistryProvider) && descriptor.ImplementationType == typeof(WebMcpPageToolRegistryProvider));
    }

    public sealed class TestHub : Hub
    {
    }

    private sealed class AdminOnlyAccessEvaluator : IWebMcpAccessEvaluator
    {
        public Task<bool> CanUseAsync(System.Security.Claims.ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(user?.IsInRole("Administrator") == true);
        }
    }
}
