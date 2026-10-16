using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Builders;

/// <summary>
/// Builder returned by <c>AddPersonas</c> that provides access to the <see cref="IServiceCollection"/> for
/// registering AI persona services, such as the persona stores.
/// </summary>
public sealed class CrestAppsAIPersonasBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsAIPersonasBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CrestAppsAIPersonasBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>
    /// Gets the <see cref="IServiceCollection"/> used to register AI persona services.
    /// </summary>
    public IServiceCollection Services { get; }
}
