using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Elasticsearch.Builders;

public sealed class CrestAppsElasticsearchBuilder
{
    public CrestAppsElasticsearchBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    public IServiceCollection Services { get; }
}
