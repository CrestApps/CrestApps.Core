using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Builders;

/// <summary>
/// Builder returned by <c>AddCrestAppsMcpClient</c> that provides access to the
/// <see cref="IServiceCollection"/> for registering Model Context Protocol (MCP) client services.
/// </summary>
public sealed class CrestAppsMcpClientBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsMcpClientBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CrestAppsMcpClientBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>
    /// Gets the <see cref="IServiceCollection"/> used to register MCP client services.
    /// </summary>
    public IServiceCollection Services { get; }
}
