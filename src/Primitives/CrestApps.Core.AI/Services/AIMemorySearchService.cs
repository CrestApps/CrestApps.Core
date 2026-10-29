using System.Collections.Concurrent;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Speech;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Represents the AI Memory Search Service.
/// </summary>
public sealed class AIMemorySearchService : IAIMemorySearchService
{
    // The store fallback scans a user's memories in process; users rarely hold more than a few dozen.
    private const int MaxStoreSearchMemories = 200;

    private static readonly ConcurrentDictionary<string, byte> _warnedIndexProfiles = new(StringComparer.OrdinalIgnoreCase);

    private readonly IServiceProvider _serviceProvider;
    private readonly ISearchIndexProfileStore _indexProfileStore;
    private readonly IAIDeploymentManager _deploymentManager;
    private readonly IAIClientFactory _aiClientFactory;
    private readonly IAIMemoryStore _memoryStore;
    private readonly ITextTokenizer _tokenizer;
    private readonly IOptionsMonitor<AIMemoryOptions> _memoryOptions;
    private readonly ILogger<AIMemorySearchService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIMemorySearchService"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="indexProfileStore">The index profile store.</param>
    /// <param name="deploymentManager">The deployment manager.</param>
    /// <param name="aiClientFactory">The ai client factory.</param>
    /// <param name="memoryStore">The memory store searched when no vector index is usable.</param>
    /// <param name="tokenizer">The tokenizer used to match terms when searching the memory store.</param>
    /// <param name="memoryOptions">The memory options.</param>
    /// <param name="logger">The logger.</param>
    public AIMemorySearchService(
        IServiceProvider serviceProvider,
        ISearchIndexProfileStore indexProfileStore,
        IAIDeploymentManager deploymentManager,
        IAIClientFactory aiClientFactory,
        IAIMemoryStore memoryStore,
        ITextTokenizer tokenizer,
        IOptionsMonitor<AIMemoryOptions> memoryOptions,
        ILogger<AIMemorySearchService> logger)
    {
        _serviceProvider = serviceProvider;
        _indexProfileStore = indexProfileStore;
        _deploymentManager = deploymentManager;
        _aiClientFactory = aiClientFactory;
        _memoryStore = memoryStore;
        _tokenizer = tokenizer;
        _memoryOptions = memoryOptions;
        _logger = logger;
    }

    /// <summary>
    /// Searchs the operation.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="queries">The queries.</param>
    /// <param name="requestedTopN">The requested top n.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<IEnumerable<AIMemorySearchResult>> SearchAsync(
        string userId,
        IEnumerable<string> queries,
        int? requestedTopN,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogDebug("Skipping AI memory search because the user ID is missing.");

            return [];
        }

        var normalizedQueries = queries?
            .Where(query => !string.IsNullOrWhiteSpace(query))
            .Select(query => query.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedQueries is not { Length: > 0 })
        {
            _logger.LogDebug("Skipping AI memory search because no non-empty queries were provided.");

            return [];
        }

        var memoryOptions = _memoryOptions.CurrentValue;
        var configuredTopN = memoryOptions.TopN > 0 ? memoryOptions.TopN : 5;
        var topN = requestedTopN.GetValueOrDefault(configuredTopN);
        topN = Math.Clamp(topN > 0 ? topN : configuredTopN, 1, 20);

        var vectorResults = await SearchIndexAsync(memoryOptions, userId, normalizedQueries, topN, cancellationToken);

        // No usable index, or an index that holds nothing for this user (for example, memories saved before the
        // index was configured): fall back to the store, so a saved memory can always be found again.
        if (vectorResults is { Count: > 0 })
        {
            return vectorResults;
        }

        return await SearchStoreAsync(userId, normalizedQueries, topN);
    }

    private async Task<List<AIMemorySearchResult>> SearchIndexAsync(
        AIMemoryOptions memoryOptions,
        string userId,
        string[] normalizedQueries,
        int topN,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(memoryOptions.IndexProfileName))
        {
            _logger.LogDebug("No AI Memory index profile is configured; searching user memory through the memory store.");

            return null;
        }

        var indexProfile = await _indexProfileStore.FindByNameAsync(memoryOptions.IndexProfileName, cancellationToken);

        if (indexProfile is null || !string.Equals(indexProfile.Type, IndexProfileTypes.AIMemory, StringComparison.OrdinalIgnoreCase))
        {
            if (ShouldWarn(memoryOptions.IndexProfileName))
            {
                _logger.LogWarning(
                    "AI memory search falls back to the memory store because configured index profile '{IndexProfileName}' was not found or is not of type '{IndexProfileType}'.",
                    memoryOptions.IndexProfileName,
                    IndexProfileTypes.AIMemory);
            }

            return null;
        }

        var searchService = _serviceProvider.GetKeyedService<IMemoryVectorSearchService>(indexProfile.ProviderName);

        if (searchService is null)
        {
            if (ShouldWarn(indexProfile.Name))
            {
                _logger.LogWarning(
                    "AI memory search falls back to the memory store because provider '{ProviderName}' of index profile '{IndexProfileName}' does not have a registered memory vector-search service.",
                    indexProfile.ProviderName,
                    indexProfile.Name);
            }

            return null;
        }

        var embeddingGenerator = await CreateEmbeddingGeneratorAsync(indexProfile, _deploymentManager, _aiClientFactory);

        if (embeddingGenerator is null)
        {
            return null;
        }

        using var usageScope = AIUsageScope.Begin(purpose: AIUsagePurposes.Retrieval);
        var embeddings = await embeddingGenerator.GenerateAsync(normalizedQueries, cancellationToken: cancellationToken);

        if (embeddings is null || embeddings.Count == 0)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "AI memory search produced no embeddings for configured index profile '{IndexProfileName}'.",
                    indexProfile.Name);
            }

            return null;
        }

        var aggregatedResults = new Dictionary<string, AIMemorySearchResult>(StringComparer.OrdinalIgnoreCase);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Running AI memory search against profile '{IndexProfileName}' using provider '{ProviderName}' for {QueryCount} query candidate(s).",
                indexProfile.Name,
                indexProfile.ProviderName,
                normalizedQueries.Length);
        }

        foreach (var embedding in embeddings)
        {
            if (embedding.Vector.IsEmpty)
            {
                continue;
            }

            var results = await searchService.SearchAsync(indexProfile, embedding.Vector.ToArray(), userId, topN, cancellationToken);

            if (results == null)
            {
                continue;
            }

            foreach (var result in results)
            {
                var key = !string.IsNullOrWhiteSpace(result.MemoryId)
                    ? result.MemoryId
                    : $"{result.Name}|{result.Description}|{result.Content}";

                if (!aggregatedResults.TryGetValue(key, out var existing) || result.Score > existing.Score)
                {
                    aggregatedResults[key] = result;
                }
            }
        }

        return aggregatedResults.Values
            .OrderByDescending(result => result.Score)
            .Take(topN)
            .ToList();
    }

    /// <summary>
    /// Ranks the user's stored memories by how many of a query's terms they contain. Used when there is no
    /// usable vector index, so memory keeps working, less precisely, without one.
    /// </summary>
    private async Task<IEnumerable<AIMemorySearchResult>> SearchStoreAsync(string userId, string[] queries, int topN)
    {
        var queryTerms = queries
            .Select(_tokenizer.Tokenize)
            .Where(terms => terms.Count > 0)
            .ToArray();

        if (queryTerms.Length == 0)
        {
            return [];
        }

        var memories = await _memoryStore.GetByUserAsync(userId, MaxStoreSearchMemories);

        if (memories is not { Count: > 0 })
        {
            return [];
        }

        var results = new List<AIMemorySearchResult>();

        foreach (var memory in memories)
        {
            // Names are stable identifiers such as "preferred_name"; split them so "name" matches.
            var memoryTerms = _tokenizer.Tokenize(string.Join(' ', memory.Name?.Replace('_', ' ').Replace('-', ' '), memory.Description, memory.Content));

            if (memoryTerms.Count == 0)
            {
                continue;
            }

            var score = queryTerms.Max(terms => (float)terms.Count(memoryTerms.Contains) / terms.Count);

            if (score <= 0)
            {
                continue;
            }

            results.Add(new AIMemorySearchResult
            {
                MemoryId = memory.ItemId,
                Name = memory.Name,
                Description = memory.Description,
                Content = memory.Content,
                UpdatedUtc = memory.UpdatedUtc,
                Score = score,
            });
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "AI memory store search matched {MatchCount} of {MemoryCount} memories for {QueryCount} query candidate(s).",
                results.Count,
                memories.Count,
                queries.Length);
        }

        return results
            .OrderByDescending(result => result.Score)
            .ThenByDescending(result => result.UpdatedUtc)
            .Take(topN)
            .ToList();
    }

    private static bool ShouldWarn(string indexProfileName)
    {
        // A misconfigured index is hit on every turn; warning once per profile name is enough to be noticed.
        return _warnedIndexProfiles.TryAdd(indexProfileName ?? string.Empty, 0);
    }

    private async Task<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(
        SearchIndexProfile indexProfile,
        IAIDeploymentManager deploymentManager,
        IAIClientFactory aiClientFactory)
    {
        var deploymentName = indexProfile.GetMemoryEmbeddingDeploymentName();

        if (string.IsNullOrWhiteSpace(deploymentName))
        {
            if (ShouldWarn(indexProfile.Name))
            {
                _logger.LogWarning(
                    "AI memory search falls back to the memory store because index profile '{IndexProfileName}' is missing an embedding deployment.",
                    indexProfile.Name);
            }

            return null;
        }

        var deployment = await deploymentManager.FindByNameAsync(deploymentName);

        if (deployment is null)
        {
            if (ShouldWarn(indexProfile.Name))
            {
                _logger.LogWarning(
                    "AI memory search falls back to the memory store because index profile '{IndexProfileName}' could not resolve embedding deployment '{EmbeddingDeploymentName}'.",
                    indexProfile.Name,
                    deploymentName);
            }

            return null;
        }

        return await aiClientFactory.CreateEmbeddingGeneratorAsync(deployment);
    }
}
