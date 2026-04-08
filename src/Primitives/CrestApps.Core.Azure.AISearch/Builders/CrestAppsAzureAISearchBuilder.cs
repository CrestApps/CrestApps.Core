using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Azure.AISearch.Builders;

public sealed class CrestAppsAzureAISearchBuilder
{
    public CrestAppsAzureAISearchBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    public IServiceCollection Services { get; }
}
