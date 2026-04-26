using A2A;
using CrestApps.Core.AI.A2A.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.A2A.Services;

internal sealed class DefaultA2AAgentCardCacheService : IA2AAgentCardCacheService
{
    private static readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(15);

    private readonly IMemoryCache _memoryCache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<DefaultA2AAgentCardCacheService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultA2AAgentCardCacheService"/> class.
    /// </summary>
    /// <param name="memoryCache">The memory cache.</param>
    /// <param name="httpClientFactory">The http client factory.</param>
    /// <param name="httpContextAccessor">The http context accessor.</param>
    /// <param name="logger">The logger.</param>
    public DefaultA2AAgentCardCacheService(
        IMemoryCache memoryCache,
        IHttpClientFactory httpClientFactory,
        IHttpContextAccessor httpContextAccessor,
        ILogger<DefaultA2AAgentCardCacheService> logger)
    {
        _memoryCache = memoryCache;
        _httpClientFactory = httpClientFactory;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Gets agent card.
    /// </summary>
    /// <param name="connectionId">The connection id.</param>
    /// <param name="connection">The connection.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<AgentCard> GetAgentCardAsync(string connectionId, A2AConnection connection, CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey(connectionId);
        if (_memoryCache.TryGetValue(cacheKey, out AgentCard cachedCard))
        {
            return cachedCard;
        }

        try
        {
            var httpClient = _httpClientFactory.CreateClient(A2AConstants.HttpClientName);

            if (connection.TryGet<A2AConnectionMetadata>(out var metadata))
            {
                // Resolve the scoped auth service from the current request to avoid
                // capturing a scoped service in this singleton.
                var authService = _httpContextAccessor.HttpContext?.RequestServices.GetService<IA2AConnectionAuthService>();
                if (authService is not null)
                {
                    await authService.ConfigureHttpClientAsync(httpClient, metadata, cancellationToken);
                }
            }

            var resolver = new A2ACardResolver(new Uri(connection.Endpoint), httpClient);
            var agentCard = await resolver.GetAgentCardAsync(cancellationToken);
            if (agentCard is not null)
            {
                _memoryCache.Set(cacheKey, agentCard, _cacheDuration);
            }

            return agentCard;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch agent card from A2A host '{Endpoint}' for connection '{ConnectionId}'.", connection.Endpoint, connectionId);

            return null;
        }
    }

    /// <summary>
    /// Invalidates the operation.
    /// </summary>
    /// <param name="connectionId">The connection id.</param>
    public void Invalidate(string connectionId)
    {
        _memoryCache.Remove(GetCacheKey(connectionId));
    }

    private static string GetCacheKey(string connectionId)
    {
        return $"A2AAgentCard:{connectionId}";
    }
}
