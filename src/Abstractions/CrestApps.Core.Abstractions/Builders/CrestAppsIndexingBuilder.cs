using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Builders;

/// <summary>
/// Builder returned by <c>AddCrestAppsIndexing</c> that provides access to the
/// <see cref="IServiceCollection"/> for registering search indexing services.
/// </summary>
public sealed class CrestAppsIndexingBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsIndexingBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CrestAppsIndexingBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>
    /// Gets the <see cref="IServiceCollection"/> used to register search indexing services.
    /// </summary>
    public IServiceCollection Services { get; }
}
