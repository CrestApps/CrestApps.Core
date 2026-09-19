using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Builders;

/// <summary>
/// Builder returned by <c>AddToolInstances</c> that provides access to the
/// <see cref="IServiceCollection"/> for registering AI tool instance sources.
/// </summary>
public sealed class CrestAppsAIToolInstancesBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsAIToolInstancesBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CrestAppsAIToolInstancesBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>
    /// Gets the <see cref="IServiceCollection"/> used to register AI tool instance sources.
    /// </summary>
    public IServiceCollection Services { get; }
}
