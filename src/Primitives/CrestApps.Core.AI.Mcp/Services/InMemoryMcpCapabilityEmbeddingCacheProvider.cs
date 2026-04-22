using System.Collections.Concurrent;
using CrestApps.Core.AI.Mcp.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Mcp.Services;

internal sealed class InMemoryMcpCapabilityEmbeddingCacheProvider : IMcpCapabilityEmbeddingCacheProvider
{
    private readonly ConcurrentDictionary<string, McpCapabilityEmbeddingEntry[]> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger _logger;

    public InMemoryMcpCapabilityEmbeddingCacheProvider(ILogger<InMemoryMcpCapabilityEmbeddingCacheProvider> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<McpCapabilityEmbeddingEntry>> GetOrCreateEmbeddingsAsync(
        IReadOnlyList<McpServerCapabilities> capabilities,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(embeddingGenerator);

        var allEntries = new List<McpCapabilityEmbeddingEntry>();

        foreach (var server in capabilities)
        {
            if (string.IsNullOrEmpty(server.ConnectionId))
            {
                continue;
            }

            if (_cache.TryGetValue(server.ConnectionId, out var cached))
            {
                allEntries.AddRange(cached);
                continue;
            }

            var pendingTexts = new List<string>();
            var pendingMeta = new List<(string Name, string Description, McpCapabilityType Type)>();

            AddCapabilities(server.Tools, McpCapabilityType.Tool, pendingTexts, pendingMeta);
            AddCapabilities(server.Prompts, McpCapabilityType.Prompt, pendingTexts, pendingMeta);
            AddCapabilities(server.Resources, McpCapabilityType.Resource, pendingTexts, pendingMeta);
            AddCapabilities(server.ResourceTemplates, McpCapabilityType.ResourceTemplate, pendingTexts, pendingMeta);

            if (pendingTexts.Count == 0)
            {
                _cache[server.ConnectionId] = [];
                continue;
            }

            try
            {
                var embeddings = await embeddingGenerator.GenerateAsync(pendingTexts, cancellationToken: cancellationToken);

                if (embeddings is null || embeddings.Count != pendingTexts.Count)
                {
                    _logger.LogWarning(
                        "Embedding generation returned unexpected count for MCP connection '{ConnectionId}'. Expected {Expected}, got {Actual}.",
                        server.ConnectionId,
                        pendingTexts.Count,
                        embeddings?.Count ?? 0);
                    continue;
                }

                var entries = new McpCapabilityEmbeddingEntry[pendingTexts.Count];

                for (var i = 0; i < pendingTexts.Count; i++)
                {
                    entries[i] = new McpCapabilityEmbeddingEntry
                    {
                        ConnectionId = server.ConnectionId,
                        ConnectionDisplayText = server.ConnectionDisplayText,
                        CapabilityName = pendingMeta[i].Name,
                        CapabilityDescription = pendingMeta[i].Description,
                        CapabilityType = pendingMeta[i].Type,
                        Embedding = DefaultMcpCapabilityResolver.NormalizeL2(embeddings[i].Vector.ToArray()),
                    };
                }

                _cache[server.ConnectionId] = entries;
                allEntries.AddRange(entries);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to generate embeddings for MCP connection '{ConnectionId}'. Capabilities from this connection will be excluded from semantic matching.",
                    server.ConnectionId);
            }
        }

        return allEntries;
    }

    public void Invalidate(string connectionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionId);
        _cache.TryRemove(connectionId, out _);
    }

    private static void AddCapabilities(
        IReadOnlyList<McpServerCapability> items,
        McpCapabilityType type,
        List<string> texts,
        List<(string Name, string Description, McpCapabilityType Type)> meta)
    {
        if (items is null)
        {
            return;
        }

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
            {
                continue;
            }

            texts.Add(string.IsNullOrWhiteSpace(item.Description) ? item.Name : $"{item.Name}: {item.Description}");
            meta.Add((item.Name, item.Description ?? string.Empty, type));
        }
    }
}
