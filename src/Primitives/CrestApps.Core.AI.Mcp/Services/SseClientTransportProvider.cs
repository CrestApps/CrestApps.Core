using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Services;
using ModelContextProtocol.Client;

namespace CrestApps.Core.AI.Mcp.Services;

/// <summary>
/// Represents the sse Client Transport Provider.
/// </summary>
public sealed class SseClientTransportProvider : IMcpClientTransportProvider
{
    private readonly IConnectionAuthHeaderBuilder _authHeaderBuilder;

    /// <summary>
    /// Initializes a new instance of the <see cref="SseClientTransportProvider"/> class.
    /// </summary>
    /// <param name="authHeaderBuilder">The auth header builder.</param>
    public SseClientTransportProvider(IConnectionAuthHeaderBuilder authHeaderBuilder)
    {
        _authHeaderBuilder = authHeaderBuilder;
    }

    /// <summary>
    /// Determines whether handle.
    /// </summary>
    /// <param name="connection">The connection.</param>
    public bool CanHandle(McpConnection connection)
    {
        return connection.Source == McpConstants.TransportTypes.Sse;
    }

    /// <summary>
    /// Gets the operation.
    /// </summary>
    /// <param name="connection">The connection.</param>
    public async Task<IClientTransport> GetAsync(McpConnection connection)
    {
        if (!connection.TryGet<SseMcpConnectionMetadata>(out var metadata))
        {
            return null;
        }

        var headers = await _authHeaderBuilder.BuildHeadersAsync(metadata, McpConstants.DataProtectionPurpose);

return new HttpClientTransport(new HttpClientTransportOptions { Endpoint = metadata.Endpoint, AdditionalHeaders = headers, });
    }
}
