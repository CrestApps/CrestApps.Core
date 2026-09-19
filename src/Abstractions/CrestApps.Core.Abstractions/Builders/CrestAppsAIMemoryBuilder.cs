using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Builders;

/// <summary>
/// Builder returned by <c>AddCrestAppsAIMemory</c> that provides access to the
/// <see cref="IServiceCollection"/> for registering AI memory services.
/// </summary>
public sealed class CrestAppsAIMemoryBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsAIMemoryBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CrestAppsAIMemoryBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>
    /// Gets the <see cref="IServiceCollection"/> used to register AI memory services.
    /// </summary>
    public IServiceCollection Services { get; }
}
