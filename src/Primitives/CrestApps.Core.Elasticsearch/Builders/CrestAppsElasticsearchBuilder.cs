using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Elasticsearch.Builders;

/// <summary>
/// Represents the crest Apps Elasticsearch Builder.
/// </summary>
public sealed class CrestAppsElasticsearchBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsElasticsearchBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
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
