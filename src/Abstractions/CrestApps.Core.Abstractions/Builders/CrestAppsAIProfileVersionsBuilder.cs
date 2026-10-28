using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Builders;

/// <summary>
/// Builder returned by <c>AddProfileVersions</c> that provides access to the <see cref="IServiceCollection"/> for
/// registering AI profile version services, such as the version store.
/// </summary>
public sealed class CrestAppsAIProfileVersionsBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsAIProfileVersionsBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CrestAppsAIProfileVersionsBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>
    /// Gets the <see cref="IServiceCollection"/> used to register AI profile version services.
    /// </summary>
    public IServiceCollection Services { get; }
}
