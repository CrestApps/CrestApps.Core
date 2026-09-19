using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Builders;

/// <summary>
/// Builder returned by <c>AddCrestAppsMcpServer</c> that provides access to the
/// <see cref="IServiceCollection"/> for registering Model Context Protocol (MCP) server services.
/// </summary>
public sealed class CrestAppsMcpServerBuilder
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CrestAppsMcpServerBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public CrestAppsMcpServerBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>
    /// Gets the <see cref="IServiceCollection"/> used to register MCP server services.
    /// </summary>
    public IServiceCollection Services { get; }
}
