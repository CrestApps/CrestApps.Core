using CrestApps.Core.AI.Mcp.Models;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace CrestApps.Core.AI.Mcp;

/// <summary>
/// Represents the MCP Service.
/// </summary>
public sealed class McpService
{
    private readonly IEnumerable<IMcpClientTransportProvider> _providers;
    private readonly ILogger<McpService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpService"/> class.
    /// </summary>
    /// <param name="providers">The providers.</param>
    /// <param name="logger">The logger.</param>
    public McpService(
        IEnumerable<IMcpClientTransportProvider> providers,
        ILogger<McpService> logger)
    {
        _providers = providers;
        _logger = logger;
    }

    /// <summary>
    /// Gets or create client.
    /// </summary>
    /// <param name="connection">The connection.</param>
    public async Task<McpClient> GetOrCreateClientAsync(McpConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        IClientTransport transport = null;

        foreach (var provider in _providers)
        {
            if (!provider.CanHandle(connection))
            {
                continue;
            }

            transport = await provider.GetAsync(connection);
        }

        if (transport is null)
        {
            _logger.LogWarning("Unable to find an implementation of '{TypeName}' that supports the connection. Not supported transport type.", nameof(IMcpClientTransportProvider));

            return null;
        }

        return await McpClient.CreateAsync(transport);
    }
}
