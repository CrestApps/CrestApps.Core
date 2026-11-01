using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Builders;

/// <summary>
/// Configures the WebMCP services of the AI suite.
/// </summary>
public sealed class CrestAppsWebMcpBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsWebMcpBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CrestAppsWebMcpBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        Services = services;
    }

    /// <summary>
    /// Gets the service collection.
    /// </summary>
    public IServiceCollection Services { get; }
}
