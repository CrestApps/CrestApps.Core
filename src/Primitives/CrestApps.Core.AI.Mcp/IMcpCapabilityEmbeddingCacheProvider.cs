using CrestApps.Core.AI.Mcp.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Mcp;

/// <summary>
/// Caches embedding vectors for MCP capability metadata (names and descriptions).
/// Embeddings are recomputed when the underlying metadata cache is invalidated.
/// </summary>
public interface IMcpCapabilityEmbeddingCacheProvider
{
    /// <summary>
    /// Gets or creates embedding entries for all capabilities across the given servers.
    /// Entries are cached per connection ID and reused until invalidated.
    /// </summary>
    /// <param name="capabilities">The structured capability metadata from MCP servers.</param>
    /// <param name="embeddingGenerator">The embedding generator to use for computing vectors.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>All cached embedding entries, including newly computed ones.</returns>
    Task<IReadOnlyList<McpCapabilityEmbeddingEntry>> GetOrCreateEmbeddingsAsync(
        IReadOnlyList<McpServerCapabilities> capabilities,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates cached embeddings for a specific MCP connection.
    /// </summary>
    /// <param name="connectionId">The connection identifier to invalidate.</param>
    void Invalidate(string connectionId);
}
