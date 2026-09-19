using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Builders;

/// <summary>
/// Builder returned by <c>AddCrestAppsA2AClient</c> that provides access to the
/// <see cref="IServiceCollection"/> for registering Agent-to-Agent (A2A) client services.
/// </summary>
public sealed class CrestAppsA2AClientBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsA2AClientBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CrestAppsA2AClientBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>
    /// Gets the <see cref="IServiceCollection"/> used to register A2A client services.
    /// </summary>
    public IServiceCollection Services { get; }
}
