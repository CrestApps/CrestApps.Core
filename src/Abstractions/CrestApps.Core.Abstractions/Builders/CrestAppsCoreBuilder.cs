using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Builders;

/// <summary>
/// Builder returned by <c>AddCrestAppsCore</c> that provides access to the
/// <see cref="IServiceCollection"/> for registering core framework services.
/// </summary>
public sealed class CrestAppsCoreBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsCoreBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CrestAppsCoreBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>
    /// Gets the <see cref="IServiceCollection"/> used to register core framework services.
    /// </summary>
    public IServiceCollection Services { get; }
}
