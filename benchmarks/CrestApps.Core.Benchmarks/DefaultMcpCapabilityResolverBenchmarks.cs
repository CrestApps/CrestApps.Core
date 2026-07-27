using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Mcp;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Mcp.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Speech;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Measures MCP capability construction, keyword matching, duplicate merging, stable ties,
/// and bounded ranking with a legacy implementation captured before optimization.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 12)]
public class DefaultMcpCapabilityResolverBenchmarks
{
    private const string _prompt = "search customer documents and summarize account activity";

    private IMcpCapabilityResolver _currentResolver;
    private LegacyDefaultMcpCapabilityResolver _legacyResolver;
    private string[] _connectionIds;

    /// <summary>
    /// Gets or sets the total number of raw capabilities exposed by the benchmark servers.
    /// </summary>
    [Params(100, 1000, 10000)]
    public int CandidateCount { get; set; }

    /// <summary>
    /// Gets or sets the small maximum result count used after merging and ranking.
    /// </summary>
    [Params(3, 5)]
    public int TopK { get; set; }

    /// <summary>
    /// Gets or sets whether the benchmark uses the production Lucene tokenizer.
    /// The false case isolates capability construction, duplicate merging, and ranking.
    /// </summary>
    [Params(false, true)]
    public bool UseLuceneTokenizer { get; set; }

    /// <summary>
    /// Creates equivalent legacy and current resolvers over immutable in-memory servers.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var fixture = CreateFixture(CandidateCount);
        var options = new McpCapabilityResolverOptions
        {
            IncludeAllThreshold = 0,
            KeywordMatchThreshold = 0.2f,
            SimilarityThreshold = 0.3f,
            TopK = TopK,
        };
        ITextTokenizer tokenizer = UseLuceneTokenizer
            ? new LuceneTextTokenizer()
            : new CategorizedTextTokenizer();

        _connectionIds = fixture.Connections
            .Select(connection => connection.ItemId)
            .ToArray();
        _legacyResolver = new LegacyDefaultMcpCapabilityResolver(
            fixture.Store,
            fixture.MetadataProvider,
            null,
            null,
            null,
            tokenizer,
            Options.Create(options),
            NullLogger<LegacyDefaultMcpCapabilityResolver>.Instance);
        _currentResolver = CreateCurrentResolver(
            fixture.Store,
            fixture.MetadataProvider,
            tokenizer,
            options);

        var legacy = _legacyResolver
            .ResolveAsync(_prompt, null, _connectionIds)
            .GetAwaiter()
            .GetResult();
        var current = _currentResolver
            .ResolveAsync(_prompt, null, _connectionIds)
            .GetAwaiter()
            .GetResult();

        EnsureEquivalent(legacy, current);
    }

    /// <summary>
    /// Resolves capabilities with the implementation captured before optimization experiments.
    /// </summary>
    /// <returns>The resolution result.</returns>
    [Benchmark(Baseline = true)]
    public Task<McpCapabilityResolutionResult> ResolveLegacyAsync()
    {
        return _legacyResolver.ResolveAsync(_prompt, null, _connectionIds);
    }

    /// <summary>
    /// Resolves capabilities with the production implementation.
    /// </summary>
    /// <returns>The resolution result.</returns>
    [Benchmark]
    public Task<McpCapabilityResolutionResult> ResolveCurrentAsync()
    {
        return _currentResolver.ResolveAsync(_prompt, null, _connectionIds);
    }

    /// <summary>
    /// Creates realistic mixed capabilities across multiple MCP servers.
    /// </summary>
    /// <param name="candidateCount">The total raw capability count.</param>
    /// <returns>The benchmark fixture.</returns>
    private static BenchmarkFixture CreateFixture(int candidateCount)
    {
        const int serverCount = 8;

        var connections = new McpConnection[serverCount];
        var tools = CreateLists(serverCount);
        var prompts = CreateLists(serverCount);
        var resources = CreateLists(serverCount);
        var resourceTemplates = CreateLists(serverCount);

        for (var serverIndex = 0; serverIndex < serverCount; serverIndex++)
        {
            connections[serverIndex] = new McpConnection
            {
                ItemId = $"server-{serverIndex}",
                DisplayText = $"Benchmark server {serverIndex}",
                Source = "benchmark",
            };
        }

        for (var index = 0; index < candidateCount; index++)
        {
            var group = index / 12;
            var serverIndex = group % serverCount;

            switch (index % 12)
            {
                case 0:
                    tools[serverIndex].Add(new McpServerCapability
                    {
                        Name = $"searchCustomerDocuments{group}",
                        Description = "Search customer documents with filters, citations, permissions, and account metadata.",
                    });
                    break;
                case 1:
                    prompts[serverIndex].Add(new McpServerCapability
                    {
                        Name = $"summarizeAccountActivity{group}",
                        Description = "Summarize account activity, customer transactions, balances, and support history.",
                    });
                    break;
                case 2:
                    resources[serverIndex].Add(new McpServerCapability
                    {
                        Name = $"customerDocument{group}",
                        Description = "Customer document archive entry.",
                        Uri = $"customer://documents/account-activity/{group}",
                    });
                    break;
                case 3:
                    resourceTemplates[serverIndex].Add(new McpServerCapability
                    {
                        Name = $"customerDocumentTemplate{group}",
                        Description = "Loads a customer document and account activity by identifier.",
                        UriTemplate = "customer://documents/{customerId}/{documentId}",
                    });
                    break;
                case 4:
                    tools[serverIndex].Add(new McpServerCapability
                    {
                        Name = $"forecastWeather{group}",
                        Description = "Returns regional temperature, precipitation, and wind conditions.",
                    });
                    break;
                case 5:
                    tools[serverIndex].Add(new McpServerCapability
                    {
                        Name = "sharedSearch",
                        Description = "Search customer documents and summarize account activity.",
                    });
                    break;
                case 6:
                    prompts[serverIndex].Add(new McpServerCapability
                    {
                        Name = "sharedSearch",
                        Description = "Search customer documents and summarize account activity.",
                    });
                    break;
                case 7:
                    prompts[serverIndex].Add(new McpServerCapability
                    {
                        Name = "summarizeAccount",
                        Description = "Summarize customer account activity.",
                    });
                    break;
                case 8:
                    resources[serverIndex].Add(new McpServerCapability
                    {
                        Name = "accountActivity",
                        Description = "Customer account activity.",
                        Uri = "customer://account/activity",
                    });
                    break;
                case 9:
                    tools[serverIndex].Add(new McpServerCapability
                    {
                        Name = $"lookupCustomer{group}",
                        Description = null,
                    });
                    break;
                case 10:
                    prompts[serverIndex].Add(new McpServerCapability
                    {
                        Name = $"reviewDocuments{group}",
                        Description = "   ",
                    });
                    break;
                default:
                    resources[serverIndex].Add(new McpServerCapability
                    {
                        Name = $"teamCalendar{group}",
                        Description = "Schedules meetings and sends calendar invitations.",
                        Uri = $"calendar://team/{group}",
                    });
                    break;
            }
        }

        var capabilities = new McpServerCapabilities[serverCount];

        for (var serverIndex = 0; serverIndex < serverCount; serverIndex++)
        {
            capabilities[serverIndex] = new McpServerCapabilities
            {
                ConnectionId = connections[serverIndex].ItemId,
                ConnectionDisplayText = connections[serverIndex].DisplayText,
                Tools = tools[serverIndex],
                Prompts = prompts[serverIndex],
                Resources = resources[serverIndex],
                ResourceTemplates = resourceTemplates[serverIndex],
                IsHealthy = true,
            };
        }

        var store = new BenchmarkSourceCatalog(connections);
        var metadataProvider = new BenchmarkMetadataProvider(capabilities);

        return new BenchmarkFixture(connections, store, metadataProvider);
    }

    /// <summary>
    /// Creates the internal production resolver through its public interface.
    /// </summary>
    /// <param name="store">The connection store.</param>
    /// <param name="metadataProvider">The metadata provider.</param>
    /// <param name="tokenizer">The tokenizer.</param>
    /// <param name="options">The resolver options.</param>
    /// <returns>The production resolver.</returns>
    private static IMcpCapabilityResolver CreateCurrentResolver(
        ISourceCatalog<McpConnection> store,
        IMcpServerMetadataCacheProvider metadataProvider,
        ITextTokenizer tokenizer,
        McpCapabilityResolverOptions options)
    {
        var resolverType = typeof(IMcpCapabilityResolver).Assembly.GetType(
            "CrestApps.Core.AI.Mcp.Services.DefaultMcpCapabilityResolver",
            throwOnError: true);
        var loggerType = typeof(NullLogger<>).MakeGenericType(resolverType);
        var logger = Activator.CreateInstance(loggerType);
        var resolver = Activator.CreateInstance(
            resolverType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                store,
                metadataProvider,
                null,
                null,
                null,
                tokenizer,
                Options.Create(options),
                logger,
            ],
            culture: null);

        return (IMcpCapabilityResolver)resolver;
    }

    /// <summary>
    /// Creates one mutable capability list per server.
    /// </summary>
    /// <param name="serverCount">The server count.</param>
    /// <returns>The lists.</returns>
    private static List<McpServerCapability>[] CreateLists(int serverCount)
    {
        var lists = new List<McpServerCapability>[serverCount];

        for (var index = 0; index < lists.Length; index++)
        {
            lists[index] = [];
        }

        return lists;
    }

    /// <summary>
    /// Verifies exact result equivalence before benchmarks execute.
    /// </summary>
    /// <param name="legacy">The legacy result.</param>
    /// <param name="current">The current result.</param>
    private static void EnsureEquivalent(
        McpCapabilityResolutionResult legacy,
        McpCapabilityResolutionResult current)
    {
        var legacyCandidates = legacy.Candidates
            .Select(candidate => (
                candidate.ConnectionId,
                candidate.ConnectionDisplayText,
                candidate.CapabilityName,
                candidate.CapabilityDescription,
                candidate.CapabilityType,
                candidate.SimilarityScore));
        var currentCandidates = current.Candidates
            .Select(candidate => (
                candidate.ConnectionId,
                candidate.ConnectionDisplayText,
                candidate.CapabilityName,
                candidate.CapabilityDescription,
                candidate.CapabilityType,
                candidate.SimilarityScore));

        if (!legacyCandidates.SequenceEqual(currentCandidates))
        {
            throw new InvalidOperationException("Legacy and current MCP capability results differ.");
        }
    }

    private sealed record BenchmarkFixture(
        IReadOnlyList<McpConnection> Connections,
        BenchmarkSourceCatalog Store,
        BenchmarkMetadataProvider MetadataProvider);

    private sealed class BenchmarkSourceCatalog : ISourceCatalog<McpConnection>
    {
        private readonly IReadOnlyList<McpConnection> _connections;

        /// <summary>
        /// Initializes an immutable benchmark catalog.
        /// </summary>
        /// <param name="connections">The connections.</param>
        public BenchmarkSourceCatalog(IReadOnlyList<McpConnection> connections)
        {
            _connections = connections;
        }

        /// <summary>
        /// Returns connections matching the requested identifiers in catalog order.
        /// </summary>
        /// <param name="ids">The identifiers.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The matching connections.</returns>
        public ValueTask<IReadOnlyCollection<McpConnection>> GetAsync(
            IEnumerable<string> ids,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var idSet = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
            IReadOnlyCollection<McpConnection> result = _connections
                .Where(connection => idSet.Contains(connection.ItemId))
                .ToArray();

            return ValueTask.FromResult(result);
        }

        /// <summary>
        /// Throws because source filtering is not used by the benchmark.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask<IReadOnlyCollection<McpConnection>> GetAsync(
            string source,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Throws because full enumeration is not used by the benchmark.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask<IReadOnlyCollection<McpConnection>> GetAllAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Throws because identifier lookup is not used by the benchmark.
        /// </summary>
        /// <param name="id">The identifier.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask<McpConnection> FindByIdAsync(
            string id,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Throws because paging is not used by the benchmark.
        /// </summary>
        /// <typeparam name="TQuery">The query type.</typeparam>
        /// <param name="page">The page.</param>
        /// <param name="pageSize">The page size.</param>
        /// <param name="context">The query context.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask<PageResult<McpConnection>> PageAsync<TQuery>(
            int page,
            int pageSize,
            TQuery context,
            CancellationToken cancellationToken = default)
            where TQuery : QueryContext
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Throws because writes are not used by the benchmark.
        /// </summary>
        /// <param name="entry">The entry.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask CreateAsync(
            McpConnection entry,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Throws because writes are not used by the benchmark.
        /// </summary>
        /// <param name="entry">The entry.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask UpdateAsync(
            McpConnection entry,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Throws because writes are not used by the benchmark.
        /// </summary>
        /// <param name="entry">The entry.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public ValueTask<bool> DeleteAsync(
            McpConnection entry,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class BenchmarkMetadataProvider : IMcpServerMetadataCacheProvider
    {
        private readonly Dictionary<string, McpServerCapabilities> _capabilities;

        /// <summary>
        /// Initializes an immutable benchmark metadata provider.
        /// </summary>
        /// <param name="capabilities">The server capabilities.</param>
        public BenchmarkMetadataProvider(IEnumerable<McpServerCapabilities> capabilities)
        {
            _capabilities = capabilities.ToDictionary(
                server => server.ConnectionId,
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns metadata for the requested connection.
        /// </summary>
        /// <param name="connection">The connection.</param>
        /// <returns>The server capabilities.</returns>
        public Task<McpServerCapabilities> GetCapabilitiesAsync(McpConnection connection)
        {
            _capabilities.TryGetValue(connection.ItemId, out var capabilities);

            return Task.FromResult(capabilities);
        }

        /// <summary>
        /// Completes because benchmark metadata is immutable.
        /// </summary>
        /// <param name="connectionId">The connection identifier.</param>
        public Task InvalidateAsync(string connectionId)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class CategorizedTextTokenizer : ITextTokenizer
    {
        private static readonly HashSet<string> _promptTokens =
        [
            "search",
            "customer",
            "document",
            "summarize",
            "account",
            "activity",
        ];

        private static readonly HashSet<string> _strongTokens =
        [
            "search",
            "customer",
            "document",
            "summarize",
            "account",
            "activity",
            "citation",
            "permission",
        ];

        private static readonly HashSet<string> _summaryTokens =
        [
            "customer",
            "summarize",
            "account",
            "activity",
            "transaction",
            "balance",
        ];

        private static readonly HashSet<string> _resourceTokens =
        [
            "customer",
            "document",
            "account",
            "activity",
            "archive",
            "identifier",
        ];

        private static readonly HashSet<string> _weakTokens =
        [
            "customer",
            "lookup",
            "review",
            "record",
        ];

        /// <summary>
        /// Returns categorized precomputed tokens to isolate merge and ranking costs.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns>The categorized token set.</returns>
        public HashSet<string> Tokenize(string text)
        {
            if (string.Equals(text, _prompt, StringComparison.Ordinal))
            {
                return _promptTokens;
            }

            if (text.Contains("sharedSearch", StringComparison.Ordinal) ||
                text.Contains("searchCustomerDocuments", StringComparison.Ordinal))
            {
                return _strongTokens;
            }

            if (text.Contains("summarizeAccount", StringComparison.Ordinal))
            {
                return _summaryTokens;
            }

            if (text.Contains("customerDocument", StringComparison.Ordinal) ||
                text.Contains("accountActivity", StringComparison.Ordinal))
            {
                return _resourceTokens;
            }

            if (text.Contains("lookupCustomer", StringComparison.Ordinal) ||
                text.Contains("reviewDocuments", StringComparison.Ordinal))
            {
                return _weakTokens;
            }

            return [];
        }
    }
}

/// <summary>
/// Captures the production resolver implementation before optimization experiments.
/// </summary>
internal sealed class LegacyDefaultMcpCapabilityResolver : IMcpCapabilityResolver
{
    private readonly ISourceCatalog<McpConnection> _store;
    private readonly IMcpServerMetadataCacheProvider _metadataProvider;
    private readonly IMcpCapabilityEmbeddingCacheProvider _embeddingCache;
    private readonly IAIClientFactory _aiClientFactory;
    private readonly IAIDeploymentManager _deploymentManager;
    private readonly ITextTokenizer _tokenizer;
    private readonly McpCapabilityResolverOptions _resolverOptions;
    private readonly ILogger<LegacyDefaultMcpCapabilityResolver> _logger;

    /// <summary>
    /// Initializes the captured legacy resolver.
    /// </summary>
    /// <param name="store">The connection store.</param>
    /// <param name="metadataProvider">The metadata provider.</param>
    /// <param name="embeddingCache">The embedding cache.</param>
    /// <param name="aiClientFactory">The AI client factory.</param>
    /// <param name="deploymentManager">The deployment manager.</param>
    /// <param name="tokenizer">The tokenizer.</param>
    /// <param name="resolverOptions">The resolver options.</param>
    /// <param name="logger">The logger.</param>
    public LegacyDefaultMcpCapabilityResolver(
        ISourceCatalog<McpConnection> store,
        IMcpServerMetadataCacheProvider metadataProvider,
        IMcpCapabilityEmbeddingCacheProvider embeddingCache,
        IAIClientFactory aiClientFactory,
        IAIDeploymentManager deploymentManager,
        ITextTokenizer tokenizer,
        IOptions<McpCapabilityResolverOptions> resolverOptions,
        ILogger<LegacyDefaultMcpCapabilityResolver> logger)
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

    /// <summary>
    /// Resolves matching MCP capabilities with the captured implementation.
    /// </summary>
    /// <param name="prompt">The prompt.</param>
    /// <param name="clientName">The AI client name.</param>
    /// <param name="mcpConnectionIds">The connection identifiers.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resolution result.</returns>
    public async Task<McpCapabilityResolutionResult> ResolveAsync(
        string prompt,
        string clientName,
        string[] mcpConnectionIds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt) || mcpConnectionIds is null || mcpConnectionIds.Length == 0)
        {
            return McpCapabilityResolutionResult.Empty;
        }

        try
        {
            var connections = await _store.GetAsync(mcpConnectionIds, cancellationToken);

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
                clientName,
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
        string clientName,
        List<McpServerCapabilities> capabilitiesList,
        List<CapabilityEntry> entries,
        CancellationToken cancellationToken)
    {
        var embeddingGenerator = await CreateEmbeddingGeneratorAsync(clientName);

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

    private List<McpCapabilityCandidate> KeywordMatch(
        string prompt,
        List<CapabilityEntry> entries)
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

        static void AddToMap(
            Dictionary<string, McpCapabilityCandidate> map,
            List<McpCapabilityCandidate> candidates)
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

    private async Task<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(
        string clientName)
    {
        if (string.IsNullOrEmpty(clientName))
        {
            return null;
        }

        var deployment = await _deploymentManager.ResolveOrDefaultAsync(
            AIDeploymentPurpose.Embedding,
            clientName: clientName);

        if (deployment is null)
        {
            return null;
        }

        return await _aiClientFactory.CreateEmbeddingGeneratorAsync(deployment);
    }

    private static List<CapabilityEntry> BuildCapabilityEntries(
        List<McpServerCapabilities> capabilitiesList)
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

    private static McpCapabilityResolutionResult BuildResult(
        List<CapabilityEntry> entries,
        float score)
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

    private static float[] NormalizeL2(float[] vector)
    {
        var sumOfSquares = 0f;

        for (var index = 0; index < vector.Length; index++)
        {
            sumOfSquares += vector[index] * vector[index];
        }

        var magnitude = MathF.Sqrt(sumOfSquares);

        if (magnitude == 0f)
        {
            return vector;
        }

        var normalized = new float[vector.Length];

        for (var index = 0; index < vector.Length; index++)
        {
            normalized[index] = vector[index] / magnitude;
        }

        return normalized;
    }

    private static float DotProduct(float[] vectorA, float[] vectorB)
    {
        if (vectorA.Length != vectorB.Length || vectorA.Length == 0)
        {
            return 0f;
        }

        var result = 0f;

        for (var index = 0; index < vectorA.Length; index++)
        {
            result += vectorA[index] * vectorB[index];
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
