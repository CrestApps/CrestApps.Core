using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Services;
using ModelContextProtocol.Client;

namespace CrestApps.Core.AI.Mcp.Services;

public sealed class SseClientTransportProvider : IMcpClientTransportProvider
{
    private readonly IConnectionAuthHeaderBuilder _authHeaderBuilder;

    public SseClientTransportProvider(IConnectionAuthHeaderBuilder authHeaderBuilder)
    {
        _authHeaderBuilder = authHeaderBuilder;
    }

    public bool CanHandle(McpConnection connection)
    {
        return connection.Source == McpConstants.TransportTypes.Sse;
    }

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
