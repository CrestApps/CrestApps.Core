using System.Text.Json;
using CrestApps.Core.AI.Mcp.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Mcp.Services;

internal sealed class DefaultMcpServerMetadataProvider : IMcpServerMetadataCacheProvider
{
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);
    private const string CacheKeyPrefix = "McpServerCapabilities_";

    private readonly McpService _mcpService;
    private readonly IDistributedCache _cache;
    private readonly IMcpCapabilityEmbeddingCacheProvider _embeddingCache;
    private readonly TimeProvider _timeProvider;
    private readonly McpMetadataCacheOptions _cacheOptions;
    private readonly ILogger<DefaultMcpServerMetadataProvider> _logger;

    public DefaultMcpServerMetadataProvider(
        McpService mcpService,
        IDistributedCache cache,
        IMcpCapabilityEmbeddingCacheProvider embeddingCache,
        IOptions<McpMetadataCacheOptions> cacheOptions,
        TimeProvider timeProvider,
        ILogger<DefaultMcpServerMetadataProvider> logger)
    {
        _mcpService = mcpService;
        _cache = cache;
        _embeddingCache = embeddingCache;
        _timeProvider = timeProvider;
        _cacheOptions = cacheOptions.Value;
        _logger = logger;
    }

    public async Task<McpServerCapabilities> GetCapabilitiesAsync(McpConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var cacheKey = CacheKeyPrefix + connection.ItemId;
        var cached = await TryGetCachedCapabilitiesAsync(cacheKey);

        if (cached is not null)
        {
            return cached;
        }

        var capabilities = await FetchCapabilitiesAsync(connection);

        if (capabilities is not null)
        {
            await CacheCapabilitiesAsync(cacheKey, capabilities);
        }

        return capabilities;
    }

    public async Task InvalidateAsync(string connectionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionId);

        await _cache.RemoveAsync(CacheKeyPrefix + connectionId);
        _embeddingCache.Invalidate(connectionId);
    }

    private async Task<McpServerCapabilities> TryGetCachedCapabilitiesAsync(string cacheKey)
    {
        try
        {
            var cachedBytes = await _cache.GetAsync(cacheKey);

            if (cachedBytes is null || cachedBytes.Length == 0)
            {
                return null;
            }

            return JsonSerializer.Deserialize<McpServerCapabilities>(cachedBytes, _serializerOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read MCP server metadata cache entry '{CacheKey}'.", cacheKey);
            return null;
        }
    }

    private async Task CacheCapabilitiesAsync(string cacheKey, McpServerCapabilities capabilities)
    {
        try
        {
            var cacheEntryOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _cacheOptions.GetCacheDuration(),
            };

            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(capabilities, _serializerOptions);
            await _cache.SetAsync(cacheKey, jsonBytes, cacheEntryOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache MCP server metadata for '{CacheKey}'.", cacheKey);
        }
    }

    private async Task<McpServerCapabilities> FetchCapabilitiesAsync(McpConnection connection)
    {
        var capabilities = new McpServerCapabilities
        {
            ConnectionId = connection.ItemId,
            ConnectionDisplayText = connection.DisplayText,
            FetchedUtc = _timeProvider.GetUtcNow().UtcDateTime,
        };

        try
        {
            var client = await _mcpService.GetOrCreateClientAsync(connection);

            if (client is null)
            {
                capabilities.IsHealthy = false;
                return capabilities;
            }

            var tools = new List<McpServerCapability>();
            var prompts = new List<McpServerCapability>();
            var resources = new List<McpServerCapability>();
            var resourceTemplates = new List<McpServerCapability>();

            try
            {
                foreach (var tool in await client.ListToolsAsync())
                {
                    tools.Add(new McpServerCapability
                    {
                        Name = tool.Name,
                        Description = tool.Description,
                        InputSchema = tool.JsonSchema is JsonElement schema ? schema : null,
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to list tools for MCP connection '{ConnectionId}'.", connection.ItemId);
            }

            try
            {
                foreach (var prompt in await client.ListPromptsAsync())
                {
                    prompts.Add(new McpServerCapability
                    {
                        Name = prompt.Name,
                        Description = prompt.Description,
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to list prompts for MCP connection '{ConnectionId}'.", connection.ItemId);
            }

            try
            {
                foreach (var resource in await client.ListResourcesAsync())
                {
                    resources.Add(new McpServerCapability
                    {
                        Name = resource.Name,
                        Description = resource.Description,
                        MimeType = resource.MimeType,
                        Uri = resource.Uri,
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to list resources for MCP connection '{ConnectionId}'.", connection.ItemId);
            }

            try
            {
                foreach (var template in await client.ListResourceTemplatesAsync())
                {
                    resourceTemplates.Add(new McpServerCapability
                    {
                        Name = template.Name,
                        Description = template.Description,
                        MimeType = template.MimeType,
                        UriTemplate = template.UriTemplate,
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to list resource templates for MCP connection '{ConnectionId}'.", connection.ItemId);
            }

            capabilities.Tools = tools;
            capabilities.Prompts = prompts;
            capabilities.Resources = resources;
            capabilities.ResourceTemplates = resourceTemplates;
            capabilities.IsHealthy = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to fetch capabilities for MCP connection '{ConnectionId}' ('{ConnectionName}').",
                connection.ItemId,
                connection.DisplayText);

            capabilities.IsHealthy = false;
        }

        return capabilities;
    }
}
