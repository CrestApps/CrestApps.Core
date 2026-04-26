using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Elasticsearch.Builders;

/// <summary>
/// Represents the crest Apps Elasticsearch Builder.
/// </summary>
public sealed class CrestAppsElasticsearchBuilder
{
    public CrestAppsElasticsearchBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>
    /// Gets the services.
    /// </summary>
    public IServiceCollection Services { get; }
}
