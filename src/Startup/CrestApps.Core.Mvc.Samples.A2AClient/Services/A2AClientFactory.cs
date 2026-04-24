using System.Net.Http.Headers;
using System.Text.Json;
using A2A;
using CrestApps.Core.Startup.Shared.Services;

namespace CrestApps.Core.Mvc.Samples.A2AClient.Services;

public sealed class A2AClientFactory
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SampleServerSelectionService _serverSelection;

    public A2AClientFactory(
        IHttpContextAccessor httpContextAccessor,
        IHttpClientFactory httpClientFactory,
        SampleServerSelectionService serverSelection)
    {
        _httpContextAccessor = httpContextAccessor;
        _httpClientFactory = httpClientFactory;
        _serverSelection = serverSelection;
    }

    public A2A.A2AClient Create(string agentUrl = null)
    {
        var server = GetSelectedServer();
        var url = agentUrl ?? server.Endpoint.TrimEnd('/') + "/a2a";
        var httpClient = _httpClientFactory.CreateClient();
        ApplyAuthentication(httpClient, server);

        return new A2A.A2AClient(new Uri(url), httpClient);
    }

    public async Task<List<AgentCard>> GetAgentCardsAsync(CancellationToken cancellationToken)
    {
        var server = GetSelectedServer();
        var httpClient = _httpClientFactory.CreateClient();
        ApplyAuthentication(httpClient, server);

        var cardUrl = $"{server.Endpoint.TrimEnd('/')}/.well-known/agent-card.json";
        var response = await httpClient.GetAsync(cardUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        // Try to parse as array first (multi-agent mode), then as single card (skill mode).
        try
        {
            var cards = JsonSerializer.Deserialize<List<AgentCard>>(json, _jsonOptions);

            if (cards is not null)
            {
                return cards;
            }
        }
        catch (JsonException)
        {
            // Not an array, try single card.
        }

        var singleCard = JsonSerializer.Deserialize<AgentCard>(json, _jsonOptions);

        return singleCard is not null ? [singleCard] : [];
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

    private static void ApplyAuthentication(HttpClient httpClient, ConfiguredServerEndpoint server)
    {
        if (string.IsNullOrWhiteSpace(server.ApiKey))
        {
            return;
        }

        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.ApiKey);
    }
}
