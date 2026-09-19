using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Builders;

/// <summary>
/// Builder returned by <c>AddCrestAppsAISuite</c> that provides access to the
/// <see cref="IServiceCollection"/> for registering AI suite services.
/// </summary>
public sealed class CrestAppsAISuiteBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsAISuiteBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CrestAppsAISuiteBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>
    /// Gets the <see cref="IServiceCollection"/> used to register AI suite services.
    /// </summary>
    public IServiceCollection Services { get; }
}
