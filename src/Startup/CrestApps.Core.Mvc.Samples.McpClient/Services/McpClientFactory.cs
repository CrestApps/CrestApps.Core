using System.Net.Http.Headers;
using CrestApps.Core.Startup.Shared.Services;
using Microsoft.Net.Http.Headers;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CrestApps.Core.Mvc.Samples.McpClient.Services;

public sealed class McpClientFactory
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SampleServerSelectionService _serverSelection;

    public McpClientFactory(
        IHttpContextAccessor httpContextAccessor,
        ILoggerFactory loggerFactory,
        SampleServerSelectionService serverSelection)
    {
        _httpContextAccessor = httpContextAccessor;
        _loggerFactory = loggerFactory;
        _serverSelection = serverSelection;
    }

    public async Task<ModelContextProtocol.Client.McpClient> CreateAsync(CancellationToken cancellationToken)
    {
        var server = GetSelectedServer();
        var endpoint = server.Endpoint;

        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri(endpoint),
        };

        if (!string.IsNullOrWhiteSpace(server.ApiKey))
        {
            transportOptions.AdditionalHeaders = new Dictionary<string, string>
            {
                [HeaderNames.Authorization] = new AuthenticationHeaderValue("Bearer", server.ApiKey).ToString(),
            };
        }

        var transport = new HttpClientTransport(transportOptions, _loggerFactory);

        var clientOptions = new McpClientOptions
        {
            ClientInfo = new Implementation
            {
                Name = "CrestApps.Core.Mvc.Samples.McpClient",
                Version = "1.0.0",
            },
        };

        try
        {
            return await ModelContextProtocol.Client.McpClient.CreateAsync(transport, clientOptions, _loggerFactory, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                $"The MCP server at '{endpoint}' returned a 404 Not Found response. " +
                $"Please ensure the selected server '{server.DisplayName}' exposes an MCP endpoint.", ex);
        }
    }

    public ConfiguredServerEndpoint GetSelectedServer()
    {
        var httpContext = _httpContextAccessor.HttpContext;

        if (httpContext == null)
        {
            throw new InvalidOperationException("The current HTTP context is not available.");
        }

        return _serverSelection.GetCurrent(httpContext);
    }
}
