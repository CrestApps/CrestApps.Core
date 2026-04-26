using CrestApps.Core.AI.Mcp.Models;
using ModelContextProtocol.Client;

namespace CrestApps.Core.AI.Mcp.Services;

/// <summary>
/// Represents the stdio Client Transport Provider.
/// </summary>
public sealed class StdioClientTransportProvider : IMcpClientTransportProvider
{
    /// <summary>
    /// Determines whether handle.
    /// </summary>
    /// <param name="connection">The connection.</param>
    public bool CanHandle(McpConnection connection)
    {
        return connection.Source == McpConstants.TransportTypes.StdIo;
    }

    /// <summary>
    /// Gets the operation.
    /// </summary>
    /// <param name="connection">The connection.</param>
    public Task<IClientTransport> GetAsync(McpConnection connection)
    {
        if (!connection.TryGet<StdioMcpConnectionMetadata>(out var metadata))
        {
            return Task.FromResult<IClientTransport>(null);
        }

        var transport = new StdioClientTransport(new StdioClientTransportOptions { Name = connection.DisplayText, Command = metadata.Command, Arguments = metadata.Arguments, WorkingDirectory = metadata.WorkingDirectory, EnvironmentVariables = metadata.EnvironmentVariables, });

        return Task.FromResult<IClientTransport>(transport);
    }
}
