using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Speech;
using CrestApps.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Mcp.Services;

internal sealed class DefaultMcpCapabilityResolver : IMcpCapabilityResolver
{
    private readonly ISourceCatalog<McpConnection> _store;
    private readonly IMcpServerMetadataCacheProvider _metadataProvider;
    private readonly IMcpCapabilityEmbeddingCacheProvider _embeddingCache;
    private readonly IAIClientFactory _aiClientFactory;
    private readonly IAIDeploymentManager _deploymentManager;
    private readonly ITextTokenizer _tokenizer;
    private readonly McpCapabilityResolverOptions _resolverOptions;
    private readonly ILogger<DefaultMcpCapabilityResolver> _logger;

    public DefaultMcpCapabilityResolver(
        ISourceCatalog<McpConnection> store,
        IMcpServerMetadataCacheProvider metadataProvider,
        IMcpCapabilityEmbeddingCacheProvider embeddingCache,
        IAIClientFactory aiClientFactory,
        IAIDeploymentManager deploymentManager,
        ITextTokenizer tokenizer,
        IOptions<McpCapabilityResolverOptions> resolverOptions,
        ILogger<DefaultMcpCapabilityResolver> logger)
    {
        _store = store;
        _metadataProvider = metadataProvider;
        _embeddingCache = embeddingCache;
        _aiClientFactory = aiClientFactory;
        _deploymentManager = deploymentManager;
        _tokenizer = tokenizer;
        _resolverOptions = resolverOptions.Value;
        _logger = logger;
    }

    public async Task<McpCapabilityResolutionResult> ResolveAsync(
        string prompt,
        string providerName,
        string connectionName,
        string[] mcpConnectionIds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt) || mcpConnectionIds is null || mcpConnectionIds.Length == 0)
        {
            return McpCapabilityResolutionResult.Empty;
        }

        try
        {
            var connections = await _store.GetAsync(mcpConnectionIds);

            if (connections.Count == 0)
            {
                return McpCapabilityResolutionResult.Empty;
            }

            var capabilitiesList = new List<McpServerCapabilities>();

            foreach (var connection in connections)
            {
                try
                {
                    var capabilities = await _metadataProvider.GetCapabilitiesAsync(connection);

                    if (capabilities is not null)
                    {
                        capabilitiesList.Add(capabilities);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to get capabilities from MCP connection '{ConnectionId}' during pre-intent resolution.",
                        connection.ItemId);
                }
            }

            if (capabilitiesList.Count == 0)
            {
                return McpCapabilityResolutionResult.Empty;
            }

            var entries = BuildCapabilityEntries(capabilitiesList);

            if (entries.Count == 0)
            {
                return McpCapabilityResolutionResult.Empty;
            }

            if (entries.Count <= _resolverOptions.IncludeAllThreshold)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Capability count ({Count}) is within include-all threshold ({Threshold}). Returning all capabilities.",
                        entries.Count,
                        _resolverOptions.IncludeAllThreshold);
                }

                return BuildResult(entries, 1.0f);
            }

            var embeddingCandidates = await TryEmbeddingMatchAsync(
                prompt,
                providerName,
                connectionName,
                capabilitiesList,
                entries,
                cancellationToken);

            var keywordCandidates = KeywordMatch(prompt, entries);
            var mergedCandidates = MergeCandidates(embeddingCandidates, keywordCandidates);

            if (mergedCandidates.Count > 0)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Hybrid resolution found {Count} candidate(s) (embedding: {EmbeddingCount}, keyword: {KeywordCount}).",
                        mergedCandidates.Count,
                        embeddingCandidates?.Count ?? 0,
                        keywordCandidates?.Count ?? 0);
                }

                return new McpCapabilityResolutionResult(mergedCandidates);
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("No capabilities matched user prompt via embedding or keyword strategies.");
            }

            return McpCapabilityResolutionResult.Empty;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP capability resolution failed. Continuing without pre-resolved capabilities.");
            return McpCapabilityResolutionResult.Empty;
        }
    }

    private async Task<List<McpCapabilityCandidate>> TryEmbeddingMatchAsync(
        string prompt,
        string providerName,
        string connectionName,
        List<McpServerCapabilities> capabilitiesList,
        List<CapabilityEntry> entries,
        CancellationToken cancellationToken)
    {
        var embeddingGenerator = await CreateEmbeddingGeneratorAsync(providerName, connectionName);

        if (embeddingGenerator is null)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("No embedding generator available. Falling back to keyword matching.");
            }

            return null;
        }

        var capabilityEmbeddings = await _embeddingCache.GetOrCreateEmbeddingsAsync(
            capabilitiesList,
            embeddingGenerator,
            cancellationToken);

        if (capabilityEmbeddings.Count == 0)
        {
            return null;
        }

        var promptEmbeddings = await embeddingGenerator.GenerateAsync([prompt], cancellationToken: cancellationToken);

        if (promptEmbeddings is null || promptEmbeddings.Count == 0 || promptEmbeddings[0].Vector.Length == 0)
        {
            _logger.LogWarning("Failed to generate embedding for user prompt during capability resolution.");
            return null;
        }

        var promptVector = NormalizeL2(promptEmbeddings[0].Vector.ToArray());
        var candidates = new List<McpCapabilityCandidate>();

        foreach (var embedding in capabilityEmbeddings)
        {
            var similarity = DotProduct(promptVector, embedding.Embedding);

            if (similarity >= _resolverOptions.SimilarityThreshold)
            {
                candidates.Add(new McpCapabilityCandidate
                {
                    ConnectionId = embedding.ConnectionId,
                    ConnectionDisplayText = embedding.ConnectionDisplayText,
                    CapabilityName = embedding.CapabilityName,
                    CapabilityDescription = embedding.CapabilityDescription,
                    CapabilityType = embedding.CapabilityType,
                    SimilarityScore = similarity,
                });
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Embedding-based matching found {Count} candidate(s) above threshold {Threshold}.",
                candidates.Count,
                _resolverOptions.SimilarityThreshold);
        }

        return candidates;
    }

    private List<McpCapabilityCandidate> KeywordMatch(string prompt, List<CapabilityEntry> entries)
    {
        var promptTokens = _tokenizer.Tokenize(prompt);

        if (promptTokens.Count == 0)
        {
            return null;
        }

        var candidates = new List<McpCapabilityCandidate>();

        foreach (var entry in entries)
        {
            var capabilityTokens = _tokenizer.Tokenize(entry.Text);

            if (capabilityTokens.Count == 0)
            {
                continue;
            }

            var matchCount = 0;

            foreach (var token in promptTokens)
            {
                if (capabilityTokens.Contains(token))
                {
                    matchCount++;
                }
            }

            if (matchCount == 0)
            {
                continue;
            }

            var forwardScore = (float)matchCount / promptTokens.Count;
            var reverseScore = (float)matchCount / capabilityTokens.Count;
            var score = Math.Max(forwardScore, reverseScore);

            if (score >= _resolverOptions.KeywordMatchThreshold)
            {
                candidates.Add(new McpCapabilityCandidate
                {
                    ConnectionId = entry.ConnectionId,
                    ConnectionDisplayText = entry.ConnectionDisplayText,
                    CapabilityName = entry.Name,
                    CapabilityDescription = entry.Description,
                    CapabilityType = entry.Type,
                    SimilarityScore = score,
                });
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Keyword-based matching found {Count} candidate(s) above threshold {Threshold}.",
                candidates.Count,
                _resolverOptions.KeywordMatchThreshold);
        }

        return candidates;
    }

    private List<McpCapabilityCandidate> MergeCandidates(
        List<McpCapabilityCandidate> embeddingCandidates,
        List<McpCapabilityCandidate> keywordCandidates)
    {
        var map = new Dictionary<string, McpCapabilityCandidate>(StringComparer.OrdinalIgnoreCase);

        AddToMap(map, embeddingCandidates);
        AddToMap(map, keywordCandidates);

        if (map.Count == 0)
        {
            return [];
        }

        var result = map.Values.ToList();
        result.Sort((a, b) => b.SimilarityScore.CompareTo(a.SimilarityScore));

        if (result.Count > _resolverOptions.TopK)
        {
            result.RemoveRange(_resolverOptions.TopK, result.Count - _resolverOptions.TopK);
        }

        return result;

        static void AddToMap(Dictionary<string, McpCapabilityCandidate> map, List<McpCapabilityCandidate> candidates)
        {
            if (candidates is null)
            {
                return;
            }

            foreach (var candidate in candidates)
            {
                var key = $"{candidate.ConnectionId}\0{candidate.CapabilityName}";

                if (!map.TryGetValue(key, out var existing) || candidate.SimilarityScore > existing.SimilarityScore)
                {
                    map[key] = candidate;
                }
            }
        }
    }

    private async Task<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(string providerName, string connectionName)
    {
        if (string.IsNullOrEmpty(providerName))
        {
            return null;
        }

        var deployment = await _deploymentManager.ResolveOrDefaultAsync(
            AIDeploymentType.Embedding,
            clientName: providerName,
            connectionName: connectionName);

        if (deployment is null)
        {
            return null;
        }

        return await _aiClientFactory.CreateEmbeddingGeneratorAsync(deployment);
    }

    private static List<CapabilityEntry> BuildCapabilityEntries(List<McpServerCapabilities> capabilitiesList)
    {
        var entries = new List<CapabilityEntry>();

        foreach (var server in capabilitiesList)
        {
            AddEntries(entries, server, server.Tools, McpCapabilityType.Tool);
            AddEntries(entries, server, server.Prompts, McpCapabilityType.Prompt);
            AddEntries(entries, server, server.Resources, McpCapabilityType.Resource);
            AddEntries(entries, server, server.ResourceTemplates, McpCapabilityType.ResourceTemplate);
        }

        return entries;

        static void AddEntries(
            List<CapabilityEntry> entries,
            McpServerCapabilities server,
            IReadOnlyList<McpServerCapability> items,
            McpCapabilityType type)
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

                var uriText = item.UriTemplate ?? item.Uri;
                var parts = new List<string>(3) { item.Name };

                if (!string.IsNullOrWhiteSpace(uriText))
                {
                    parts.Add(uriText);
                }

                if (!string.IsNullOrWhiteSpace(item.Description))
                {
                    parts.Add(item.Description);
                }

                entries.Add(new CapabilityEntry
                {
                    ConnectionId = server.ConnectionId,
                    ConnectionDisplayText = server.ConnectionDisplayText,
                    Name = item.Name,
                    Description = item.Description ?? string.Empty,
                    Type = type,
                    Text = string.Join(": ", parts),
                });
            }
        }
    }

    private static McpCapabilityResolutionResult BuildResult(List<CapabilityEntry> entries, float score)
    {
        var candidates = new List<McpCapabilityCandidate>(entries.Count);

        foreach (var entry in entries)
        {
            candidates.Add(new McpCapabilityCandidate
            {
                ConnectionId = entry.ConnectionId,
                ConnectionDisplayText = entry.ConnectionDisplayText,
                CapabilityName = entry.Name,
                CapabilityDescription = entry.Description,
                CapabilityType = entry.Type,
                SimilarityScore = score,
            });
        }

        return new McpCapabilityResolutionResult(candidates);
    }

    internal static float[] NormalizeL2(float[] vector)
    {
        var sumOfSquares = 0f;

        for (var i = 0; i < vector.Length; i++)
        {
            sumOfSquares += vector[i] * vector[i];
        }

        var magnitude = MathF.Sqrt(sumOfSquares);

        if (magnitude == 0f)
        {
            return vector;
        }

        var normalized = new float[vector.Length];

        for (var i = 0; i < vector.Length; i++)
        {
            normalized[i] = vector[i] / magnitude;
        }

        return normalized;
    }

    internal static float DotProduct(float[] vectorA, float[] vectorB)
    {
        if (vectorA.Length != vectorB.Length || vectorA.Length == 0)
        {
            return 0f;
        }

        var result = 0f;

        for (var i = 0; i < vectorA.Length; i++)
        {
            result += vectorA[i] * vectorB[i];
        }

        return result;
    }

    private struct CapabilityEntry
    {
        public string ConnectionId;
        public string ConnectionDisplayText;
        public string Name;
        public string Description;
        public McpCapabilityType Type;
        public string Text;
    }
}
