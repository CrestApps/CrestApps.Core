using CrestApps.Core.AI;
using CrestApps.Core.AI.Clients;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Mcp;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.AI.Mcp.Services;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Speech;
using CrestApps.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace CrestApps.Core.Tests.Core.Mcp;

public sealed class DefaultMcpCapabilityResolverTests
{
    /// <summary>
    /// Verifies that the include-all path preserves connection, capability-type, and source-list order.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WithinIncludeAllThreshold_PreservesSourceOrderingAndDuplicates()
    {
        var firstServer = CreateServer(
            "server-b",
            tools:
            [
                CreateCapability("tool-b"),
                CreateCapability("duplicate"),
            ],
            prompts:
            [
                CreateCapability("prompt-b"),
                CreateCapability("duplicate"),
            ],
            resources:
            [
                CreateCapability("resource-b", uri: "resource://server-b/item"),
            ],
            resourceTemplates:
            [
                CreateCapability("template-b", uriTemplate: "resource://server-b/{id}"),
            ]);
        var secondServer = CreateServer(
            "server-a",
            tools:
            [
                CreateCapability("tool-a"),
            ]);
        var harness = CreateHarness(
            [CreateConnection("server-b"), CreateConnection("server-a")],
            [firstServer, secondServer],
            new McpCapabilityResolverOptions
            {
                IncludeAllThreshold = 7,
                TopK = 1,
                KeywordMatchThreshold = 1f,
                SimilarityThreshold = 1f,
            });

        var result = await harness.Resolver.ResolveAsync(
            "anything",
            null,
            ["server-a", "server-b"],
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                ("server-b", "tool-b", McpCapabilityType.Tool),
                ("server-b", "duplicate", McpCapabilityType.Tool),
                ("server-b", "prompt-b", McpCapabilityType.Prompt),
                ("server-b", "duplicate", McpCapabilityType.Prompt),
                ("server-b", "resource-b", McpCapabilityType.Resource),
                ("server-b", "template-b", McpCapabilityType.ResourceTemplate),
                ("server-a", "tool-a", McpCapabilityType.Tool),
            ],
            result.Candidates.Select(candidate =>
                (candidate.ConnectionId, candidate.CapabilityName, candidate.CapabilityType)));
        Assert.All(result.Candidates, candidate => Assert.Equal(1f, candidate.SimilarityScore));
        Assert.Equal(2, result.RelevantConnectionIds.Count);
        Assert.Contains("SERVER-A", result.RelevantConnectionIds);
        Assert.Contains("SERVER-B", result.RelevantConnectionIds);
        Assert.Contains(
            harness.Logger.Entries,
            entry => entry.Level == LogLevel.Debug &&
                entry.Message.Contains("within include-all threshold", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that invalid names are skipped while descriptions retain their established normalization.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WithinIncludeAllThreshold_HandlesNullEmptyAndWhitespaceNamesAndDescriptions()
    {
        var server = CreateServer(
            "server",
            tools:
            [
                CreateCapability(null, "ignored null name"),
                CreateCapability(string.Empty, "ignored empty name"),
                CreateCapability("   ", "ignored whitespace name"),
                CreateCapability("null-description", null),
                CreateCapability("empty-description", string.Empty),
                CreateCapability("whitespace-description", "   "),
            ]);
        var harness = CreateHarness(
            [CreateConnection("server")],
            [server],
            new McpCapabilityResolverOptions
            {
                IncludeAllThreshold = 10,
            });

        var result = await harness.Resolver.ResolveAsync(
            "anything",
            null,
            ["server"],
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ["null-description", "empty-description", "whitespace-description"],
            result.Candidates.Select(candidate => candidate.CapabilityName));
        Assert.Equal(string.Empty, result.Candidates[0].CapabilityDescription);
        Assert.Equal(string.Empty, result.Candidates[1].CapabilityDescription);
        Assert.Equal("   ", result.Candidates[2].CapabilityDescription);
    }

    /// <summary>
    /// Verifies that invalid prompts and connection lists short-circuit without catalog access.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WithInvalidInput_ReturnsSharedEmptyResultWithoutDependencies()
    {
        var harness = CreateHarness([], []);

        var nullPrompt = await harness.Resolver.ResolveAsync(
            null,
            "client",
            ["server"],
            TestContext.Current.CancellationToken);
        var emptyPrompt = await harness.Resolver.ResolveAsync(
            string.Empty,
            "client",
            ["server"],
            TestContext.Current.CancellationToken);
        var whitespacePrompt = await harness.Resolver.ResolveAsync(
            "   ",
            "client",
            ["server"],
            TestContext.Current.CancellationToken);
        var nullConnections = await harness.Resolver.ResolveAsync(
            "query",
            "client",
            null,
            TestContext.Current.CancellationToken);
        var emptyConnections = await harness.Resolver.ResolveAsync(
            "query",
            "client",
            [],
            TestContext.Current.CancellationToken);

        Assert.Same(McpCapabilityResolutionResult.Empty, nullPrompt);
        Assert.Same(McpCapabilityResolutionResult.Empty, emptyPrompt);
        Assert.Same(McpCapabilityResolutionResult.Empty, whitespacePrompt);
        Assert.Same(McpCapabilityResolutionResult.Empty, nullConnections);
        Assert.Same(McpCapabilityResolutionResult.Empty, emptyConnections);
        harness.Store.Verify(
            store => store.GetAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that the catalog controls server filtering and receives the exact request and token.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_FiltersThroughCatalogAndPassesCancellationToken()
    {
        var requestedIds = new[] { "missing", "selected" };
        var originalIds = requestedIds.ToArray();
        var selectedConnection = CreateConnection("selected");
        IEnumerable<string> capturedIds = null;
        var capturedToken = default(CancellationToken);
        using var cancellationSource = new CancellationTokenSource();
        var harness = CreateHarness(
            [selectedConnection],
            [CreateServer("selected", tools: [CreateCapability("selected-tool")])],
            storeResolver: (ids, cancellationToken) =>
            {
                capturedIds = ids.ToArray();
                capturedToken = cancellationToken;

                return ValueTask.FromResult<IReadOnlyCollection<McpConnection>>([selectedConnection]);
            });

        var result = await harness.Resolver.ResolveAsync(
            "query",
            null,
            requestedIds,
            cancellationSource.Token);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("selected", candidate.ConnectionId);
        Assert.Equal(originalIds, capturedIds);
        Assert.Equal(originalIds, requestedIds);
        Assert.Equal(cancellationSource.Token, capturedToken);
        harness.Metadata.Verify(
            provider => provider.GetCapabilitiesAsync(
                It.Is<McpConnection>(connection => connection.ItemId == "selected")),
            Times.Once);
    }

    /// <summary>
    /// Verifies that null, failed, and unhealthy metadata sources retain their established handling.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WithUnavailableFailedAndUnhealthyMetadata_SkipsOnlyUnavailableSources()
    {
        var connections = new[]
        {
            CreateConnection("failed"),
            CreateConnection("unavailable"),
            CreateConnection("unhealthy"),
            CreateConnection("healthy"),
        };
        var unhealthy = CreateServer(
            "unhealthy",
            tools: [CreateCapability("unhealthy-tool")]);
        unhealthy.IsHealthy = false;
        var healthy = CreateServer(
            "healthy",
            tools: [CreateCapability("healthy-tool")]);
        var harness = CreateHarness(
            connections,
            [unhealthy, healthy],
            metadataResolver: connection => connection.ItemId switch
            {
                "failed" => Task.FromException<McpServerCapabilities>(
                    new InvalidOperationException("metadata failure")),
                "unavailable" => Task.FromResult<McpServerCapabilities>(null),
                "unhealthy" => Task.FromResult(unhealthy),
                _ => Task.FromResult(healthy),
            });

        var result = await harness.Resolver.ResolveAsync(
            "query",
            null,
            connections.Select(connection => connection.ItemId).ToArray(),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ["unhealthy-tool", "healthy-tool"],
            result.Candidates.Select(candidate => candidate.CapabilityName));
        Assert.Contains(
            harness.Logger.Entries,
            entry => entry.Level == LogLevel.Warning &&
                entry.Message.Contains("Failed to get capabilities", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that catalog cancellation propagates to the caller.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenCatalogIsCanceled_PropagatesCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var harness = CreateHarness(
            [],
            [],
            storeResolver: (_, cancellationToken) =>
                new ValueTask<IReadOnlyCollection<McpConnection>>(
                    Task.FromCanceled<IReadOnlyCollection<McpConnection>>(cancellationToken)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Resolver.ResolveAsync(
                "query",
                null,
                ["server"],
                cancellationSource.Token));
    }

    /// <summary>
    /// Verifies that embedding-cache cancellation propagates and receives the caller's token.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenEmbeddingCacheIsCanceled_PropagatesCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var server = CreateServer(
            "server",
            tools: [CreateCapability("tool")]);
        var harness = CreateHarness(
            [CreateConnection("server")],
            [server],
            new McpCapabilityResolverOptions
            {
                IncludeAllThreshold = 0,
            },
            embeddingDeploymentAvailable: true,
            embeddingResolver: (_, _, cancellationToken) =>
                Task.FromCanceled<IReadOnlyList<McpCapabilityEmbeddingEntry>>(cancellationToken));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Resolver.ResolveAsync(
                "query",
                "client",
                ["server"],
                cancellationSource.Token));
        harness.EmbeddingCache.Verify(
            cache => cache.GetOrCreateEmbeddingsAsync(
                It.IsAny<IReadOnlyList<McpServerCapabilities>>(),
                It.IsAny<IEmbeddingGenerator<string, Embedding<float>>>(),
                cancellationSource.Token),
            Times.Once);
    }

    /// <summary>
    /// Verifies that metadata-provider cancellation is treated as a per-server failure.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenMetadataProviderThrowsCancellation_SkipsServerAndContinues()
    {
        var canceledConnection = CreateConnection("canceled");
        var activeConnection = CreateConnection("active");
        var activeServer = CreateServer(
            "active",
            tools: [CreateCapability("active-tool")]);
        var harness = CreateHarness(
            [canceledConnection, activeConnection],
            [activeServer],
            metadataResolver: connection => connection.ItemId == "canceled"
                ? Task.FromException<McpServerCapabilities>(new OperationCanceledException())
                : Task.FromResult(activeServer));

        var result = await harness.Resolver.ResolveAsync(
            "query",
            null,
            ["canceled", "active"],
            TestContext.Current.CancellationToken);

        Assert.Equal("active-tool", Assert.Single(result.Candidates).CapabilityName);
        Assert.Contains(
            harness.Logger.Entries,
            entry => entry.Level == LogLevel.Warning &&
                entry.Exception is OperationCanceledException);
    }

    /// <summary>
    /// Verifies Lucene case normalization, camel-case splitting, stemming, and URI matching.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_KeywordMatching_PreservesCaseTokenizationAndUriBehavior()
    {
        var server = CreateServer(
            "server",
            tools:
            [
                CreateCapability("searchCustomerDocuments"),
                CreateCapability("forecastWeather"),
            ],
            resources:
            [
                CreateCapability(
                    "archive",
                    uri: "customer://documents/account-activity"),
            ]);
        var harness = CreateHarness(
            [CreateConnection("server")],
            [server],
            new McpCapabilityResolverOptions
            {
                IncludeAllThreshold = 0,
                KeywordMatchThreshold = 0.5f,
                TopK = 5,
            });

        var searchResult = await harness.Resolver.ResolveAsync(
            "SEARCHING customer documents",
            null,
            ["server"],
            TestContext.Current.CancellationToken);
        var resourceResult = await harness.Resolver.ResolveAsync(
            "account activity",
            null,
            ["server"],
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ["searchCustomerDocuments", "archive"],
            searchResult.Candidates.Select(candidate => candidate.CapabilityName));
        Assert.Equal("archive", Assert.Single(resourceResult.Candidates).CapabilityName);
    }

    /// <summary>
    /// Verifies that the keyword threshold is inclusive and preserves exact score math.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_KeywordThreshold_IncludesExactBoundaryAndExcludesLowerScore()
    {
        var promptTokens = CreateTokens(5, 5, "prompt");
        var tokenizer = new MappedTextTokenizer(new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["query"] = promptTokens,
            ["boundary"] = CreateTokens(1, 5, "boundary"),
            ["below"] = CreateTokens(0, 5, "below"),
        });
        var server = CreateServer(
            "server",
            tools:
            [
                CreateCapability("boundary"),
                CreateCapability("below"),
            ]);
        var harness = CreateHarness(
            [CreateConnection("server")],
            [server],
            new McpCapabilityResolverOptions
            {
                IncludeAllThreshold = 0,
                KeywordMatchThreshold = 0.2f,
                TopK = 5,
            },
            tokenizer);

        var result = await harness.Resolver.ResolveAsync(
            "query",
            null,
            ["server"],
            TestContext.Current.CancellationToken);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("boundary", candidate.CapabilityName);
        Assert.Equal(0.2f, candidate.SimilarityScore);
    }

    /// <summary>
    /// Verifies the exact name, URI, and description text supplied to keyword tokenization.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_KeywordMatching_UsesExactCapabilityTextComponentOrder()
    {
        var tokenizer = new MappedTextTokenizer(new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["query"] = ["customer"],
            ["resource: customer://documents/current: customer document"] = ["customer"],
        });
        var server = CreateServer(
            "server",
            resources:
            [
                CreateCapability(
                    "resource",
                    "customer document",
                    uri: "customer://documents/current"),
            ]);
        var harness = CreateHarness(
            [CreateConnection("server")],
            [server],
            new McpCapabilityResolverOptions
            {
                IncludeAllThreshold = 0,
                KeywordMatchThreshold = 1f,
                TopK = 5,
            },
            tokenizer);

        var result = await harness.Resolver.ResolveAsync(
            "query",
            null,
            ["server"],
            TestContext.Current.CancellationToken);

        Assert.Equal("resource", Assert.Single(result.Candidates).CapabilityName);
        Assert.Equal(1, tokenizer.GetCallCount(
            "resource: customer://documents/current: customer document"));
    }

    /// <summary>
    /// Verifies that the embedding threshold is inclusive and lower scores are excluded.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_EmbeddingThreshold_IncludesExactBoundaryAndExcludesLowerScore()
    {
        var server = CreateServer(
            "server",
            tools:
            [
                CreateCapability("boundary"),
                CreateCapability("below"),
            ]);
        var embeddings = new[]
        {
            CreateEmbedding("server", "boundary", McpCapabilityType.Tool, 0.3f),
            CreateEmbedding("server", "below", McpCapabilityType.Tool, 0.299f),
        };
        var harness = CreateHarness(
            [CreateConnection("server")],
            [server],
            new McpCapabilityResolverOptions
            {
                IncludeAllThreshold = 0,
                SimilarityThreshold = 0.3f,
                KeywordMatchThreshold = 1f,
                TopK = 5,
            },
            new MappedTextTokenizer(new Dictionary<string, HashSet<string>>()),
            embeddings,
            embeddingDeploymentAvailable: true);

        var result = await harness.Resolver.ResolveAsync(
            "query",
            "client",
            ["server"],
            TestContext.Current.CancellationToken);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("boundary", candidate.CapabilityName);
        Assert.Equal(0.3f, candidate.SimilarityScore);
    }

    /// <summary>
    /// Verifies merged ordering, case-insensitive identity, replacement, server scope, and tie precedence.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_HybridMerge_PreservesIdentityReplacementAndStableTieBehavior()
    {
        var promptTokens = CreateTokens(10, 10, "query");
        var tokenizer = new MappedTextTokenizer(new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["query"] = promptTokens,
            ["tie-a: keyword tie a"] = CreateTokens(5, 10, "tie-a"),
            ["tie-b: keyword tie b"] = CreateTokens(5, 10, "tie-b"),
            ["SAME: keyword best"] = CreateTokens(8, 10, "same"),
            ["keyword-only: keyword only"] = CreateTokens(9, 10, "keyword"),
            ["same: server two"] = CreateTokens(0, 10, "server-two"),
        });
        var firstServer = CreateServer(
            "server-one",
            tools:
            [
                CreateCapability("tie-a", "keyword tie a"),
                CreateCapability("tie-b", "keyword tie b"),
            ],
            prompts:
            [
                CreateCapability("SAME", "keyword best"),
            ],
            resources:
            [
                CreateCapability("keyword-only", "keyword only"),
            ]);
        var secondServer = CreateServer(
            "server-two",
            tools:
            [
                CreateCapability("same", "server two"),
            ]);
        var embeddings = new[]
        {
            CreateEmbedding(
                "server-one",
                "same",
                McpCapabilityType.Tool,
                0.6f,
                "embedding original"),
            CreateEmbedding(
                "SERVER-ONE",
                "SAME",
                McpCapabilityType.Resource,
                0.4f,
                "embedding lower duplicate"),
            CreateEmbedding(
                "server-two",
                "same",
                McpCapabilityType.Tool,
                0.7f,
                "different server"),
            CreateEmbedding(
                "server-one",
                "tie-a",
                McpCapabilityType.Tool,
                0.5f,
                "embedding tie a"),
            CreateEmbedding(
                "server-one",
                "tie-b",
                McpCapabilityType.Tool,
                0.5f,
                "embedding tie b"),
        };
        var harness = CreateHarness(
            [CreateConnection("server-one"), CreateConnection("server-two")],
            [firstServer, secondServer],
            new McpCapabilityResolverOptions
            {
                IncludeAllThreshold = 0,
                SimilarityThreshold = 0f,
                KeywordMatchThreshold = 0.1f,
                TopK = 10,
            },
            tokenizer,
            embeddings,
            embeddingDeploymentAvailable: true);

        var result = await harness.Resolver.ResolveAsync(
            "query",
            "client",
            ["server-one", "server-two"],
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                ("server-one", "keyword-only", 0.9f),
                ("server-one", "SAME", 0.8f),
                ("server-two", "same", 0.7f),
                ("server-one", "tie-a", 0.5f),
                ("server-one", "tie-b", 0.5f),
            ],
            result.Candidates.Select(candidate =>
                (candidate.ConnectionId, candidate.CapabilityName, candidate.SimilarityScore)));

        var replacement = result.Candidates[1];
        Assert.Equal(McpCapabilityType.Prompt, replacement.CapabilityType);
        Assert.Equal("keyword best", replacement.CapabilityDescription);
        Assert.Equal("embedding tie a", result.Candidates[3].CapabilityDescription);
        Assert.Equal("embedding tie b", result.Candidates[4].CapabilityDescription);
    }

    /// <summary>
    /// Verifies that a later higher embedding replaces the first value without changing tie order.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_DuplicateEmbeddingIdentity_UsesBestScoreAndFirstIdentityPosition()
    {
        var server = CreateServer(
            "server",
            tools:
            [
                CreateCapability("first"),
                CreateCapability("second"),
            ]);
        var embeddings = new[]
        {
            CreateEmbedding("server", "first", McpCapabilityType.Tool, 0.5f, "first-low"),
            CreateEmbedding("server", "second", McpCapabilityType.Tool, 0.7f, "second"),
            CreateEmbedding("SERVER", "FIRST", McpCapabilityType.Prompt, 0.7f, "first-best"),
        };
        var harness = CreateHarness(
            [CreateConnection("server")],
            [server],
            new McpCapabilityResolverOptions
            {
                IncludeAllThreshold = 0,
                SimilarityThreshold = 0f,
                KeywordMatchThreshold = 1f,
                TopK = 5,
            },
            new MappedTextTokenizer(new Dictionary<string, HashSet<string>>()),
            embeddings,
            embeddingDeploymentAvailable: true);

        var result = await harness.Resolver.ResolveAsync(
            "query",
            "client",
            ["server"],
            TestContext.Current.CancellationToken);

        Assert.Equal(["FIRST", "second"], result.Candidates.Select(candidate => candidate.CapabilityName));
        Assert.Equal(["first-best", "second"], result.Candidates.Select(candidate => candidate.CapabilityDescription));
        Assert.All(result.Candidates, candidate => Assert.Equal(0.7f, candidate.SimilarityScore));
    }

    /// <summary>
    /// Verifies top-K truncation, zero-score inclusion, and the zero limit.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_TopK_AppliesAfterRankingAndSupportsZero()
    {
        var server = CreateServer(
            "server",
            tools:
            [
                CreateCapability("one"),
                CreateCapability("two"),
                CreateCapability("three"),
                CreateCapability("four"),
                CreateCapability("zero"),
            ]);
        var embeddings = new[]
        {
            CreateEmbedding("server", "one", McpCapabilityType.Tool, 1f),
            CreateEmbedding("server", "two", McpCapabilityType.Tool, 0.8f),
            CreateEmbedding("server", "three", McpCapabilityType.Tool, 0.6f),
            CreateEmbedding("server", "four", McpCapabilityType.Tool, 0.4f),
            CreateEmbedding("server", "zero", McpCapabilityType.Tool, 0f),
        };
        var limitedHarness = CreateHarness(
            [CreateConnection("server")],
            [server],
            new McpCapabilityResolverOptions
            {
                IncludeAllThreshold = 0,
                SimilarityThreshold = 0f,
                KeywordMatchThreshold = 1f,
                TopK = 3,
            },
            new MappedTextTokenizer(new Dictionary<string, HashSet<string>>()),
            embeddings,
            embeddingDeploymentAvailable: true);
        var zeroHarness = CreateHarness(
            [CreateConnection("server")],
            [server],
            new McpCapabilityResolverOptions
            {
                IncludeAllThreshold = 0,
                SimilarityThreshold = 0f,
                KeywordMatchThreshold = 1f,
                TopK = 0,
            },
            new MappedTextTokenizer(new Dictionary<string, HashSet<string>>()),
            embeddings,
            embeddingDeploymentAvailable: true);

        var limited = await limitedHarness.Resolver.ResolveAsync(
            "query",
            "client",
            ["server"],
            TestContext.Current.CancellationToken);
        var zero = await zeroHarness.Resolver.ResolveAsync(
            "query",
            "client",
            ["server"],
            TestContext.Current.CancellationToken);

        Assert.Equal(["one", "two", "three"], limited.Candidates.Select(candidate => candidate.CapabilityName));
        Assert.Empty(zero.Candidates);
    }

    /// <summary>
    /// Verifies that an unavailable embedding deployment falls back to keyword matching with diagnostics.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WithoutEmbeddingDeployment_FallsBackToKeywordMatching()
    {
        var server = CreateServer(
            "server",
            tools:
            [
                CreateCapability("searchCustomerDocuments"),
            ]);
        var harness = CreateHarness(
            [CreateConnection("server")],
            [server],
            new McpCapabilityResolverOptions
            {
                IncludeAllThreshold = 0,
                KeywordMatchThreshold = 0.2f,
                TopK = 5,
            });

        var result = await harness.Resolver.ResolveAsync(
            "search customer documents",
            "client",
            ["server"],
            TestContext.Current.CancellationToken);

        Assert.Equal("searchCustomerDocuments", Assert.Single(result.Candidates).CapabilityName);
        Assert.Contains(
            harness.Logger.Entries,
            entry => entry.Level == LogLevel.Debug &&
                entry.Message.Contains("No embedding generator available", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that embedding-factory failures abort resolution and are converted into an empty result.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WhenEmbeddingFactoryFails_ReturnsEmptyWithoutKeywordFallback()
    {
        var server = CreateServer(
            "server",
            tools:
            [
                CreateCapability("searchCustomerDocuments"),
            ]);
        var harness = CreateHarness(
            [CreateConnection("server")],
            [server],
            new McpCapabilityResolverOptions
            {
                IncludeAllThreshold = 0,
                KeywordMatchThreshold = 0.2f,
                TopK = 5,
            },
            embeddingDeploymentAvailable: true,
            factoryResolver: _ =>
                ValueTask.FromException<IEmbeddingGenerator<string, Embedding<float>>>(
                    new InvalidOperationException("factory failure")));

        var result = await harness.Resolver.ResolveAsync(
            "search customer documents",
            "client",
            ["server"],
            TestContext.Current.CancellationToken);

        Assert.Same(McpCapabilityResolutionResult.Empty, result);
        Assert.Contains(
            harness.Logger.Entries,
            entry => entry.Level == LogLevel.Warning &&
                entry.Message.Contains("MCP capability resolution failed", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that no-match diagnostics are emitted without mutating external context.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WithNoMatches_ReturnsSharedEmptyResultAndLogsDiagnostic()
    {
        var server = CreateServer(
            "server",
            tools:
            [
                CreateCapability("forecastWeather"),
            ]);
        var harness = CreateHarness(
            [CreateConnection("server")],
            [server],
            new McpCapabilityResolverOptions
            {
                IncludeAllThreshold = 0,
                KeywordMatchThreshold = 1f,
                TopK = 5,
            });

        var result = await harness.Resolver.ResolveAsync(
            "search customer documents",
            null,
            ["server"],
            TestContext.Current.CancellationToken);

        Assert.Same(McpCapabilityResolutionResult.Empty, result);
        Assert.Contains(
            harness.Logger.Entries,
            entry => entry.Level == LogLevel.Debug &&
                entry.Message.Contains("No capabilities matched", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that exact duplicate text is tokenized once per resolution without global caching.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WithDuplicateText_ReusesTokensOnlyWithinCurrentResolution()
    {
        var tokenizer = new MappedTextTokenizer(new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["query"] = ["shared"],
            ["shared: same description"] = ["shared"],
        });
        var firstServer = CreateServer(
            "server-one",
            tools: [CreateCapability("shared", "same description")]);
        var secondServer = CreateServer(
            "server-two",
            prompts: [CreateCapability("shared", "same description")]);
        var harness = CreateHarness(
            [CreateConnection("server-one"), CreateConnection("server-two")],
            [firstServer, secondServer],
            new McpCapabilityResolverOptions
            {
                IncludeAllThreshold = 0,
                KeywordMatchThreshold = 0f,
                TopK = 5,
            },
            tokenizer);

        var first = await harness.Resolver.ResolveAsync(
            "query",
            null,
            ["server-one", "server-two"],
            TestContext.Current.CancellationToken);
        var second = await harness.Resolver.ResolveAsync(
            "query",
            null,
            ["server-one", "server-two"],
            TestContext.Current.CancellationToken);

        Assert.Equal(2, first.Candidates.Count);
        Assert.Equal(2, second.Candidates.Count);
        Assert.Equal(2, tokenizer.GetCallCount("query"));
        Assert.Equal(2, tokenizer.GetCallCount("shared: same description"));
    }

    /// <summary>
    /// Creates a resolver harness with controllable in-memory dependencies.
    /// </summary>
    /// <param name="connections">The catalog connections.</param>
    /// <param name="capabilities">The server capabilities.</param>
    /// <param name="options">The resolver options.</param>
    /// <param name="tokenizer">The tokenizer.</param>
    /// <param name="embeddings">The cached capability embeddings.</param>
    /// <param name="embeddingDeploymentAvailable">Whether an embedding deployment is available.</param>
    /// <param name="storeResolver">The optional catalog callback.</param>
    /// <param name="metadataResolver">The optional metadata callback.</param>
    /// <param name="embeddingResolver">The optional embedding-cache callback.</param>
    /// <param name="factoryResolver">The optional embedding-factory callback.</param>
    /// <returns>The configured harness.</returns>
    private static ResolverHarness CreateHarness(
        IReadOnlyCollection<McpConnection> connections,
        IReadOnlyCollection<McpServerCapabilities> capabilities,
        McpCapabilityResolverOptions options = null,
        ITextTokenizer tokenizer = null,
        IReadOnlyList<McpCapabilityEmbeddingEntry> embeddings = null,
        bool embeddingDeploymentAvailable = false,
        Func<IEnumerable<string>, CancellationToken, ValueTask<IReadOnlyCollection<McpConnection>>> storeResolver = null,
        Func<McpConnection, Task<McpServerCapabilities>> metadataResolver = null,
        Func<
            IReadOnlyList<McpServerCapabilities>,
            IEmbeddingGenerator<string, Embedding<float>>,
            CancellationToken,
            Task<IReadOnlyList<McpCapabilityEmbeddingEntry>>> embeddingResolver = null,
        Func<
            AIDeployment,
            ValueTask<IEmbeddingGenerator<string, Embedding<float>>>> factoryResolver = null)
    {
        var capabilitiesByConnection = capabilities.ToDictionary(
            capability => capability.ConnectionId,
            StringComparer.OrdinalIgnoreCase);
        var store = new Mock<ISourceCatalog<McpConnection>>();
        store
            .Setup(instance => instance.GetAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<string> ids, CancellationToken cancellationToken) =>
                storeResolver is null
                    ? ValueTask.FromResult(connections)
                    : storeResolver(ids, cancellationToken));

        var metadata = new Mock<IMcpServerMetadataCacheProvider>();
        metadata
            .Setup(instance => instance.GetCapabilitiesAsync(It.IsAny<McpConnection>()))
            .Returns((McpConnection connection) =>
            {
                if (metadataResolver is not null)
                {
                    return metadataResolver(connection);
                }

                capabilitiesByConnection.TryGetValue(connection.ItemId, out var serverCapabilities);

                return Task.FromResult(serverCapabilities);
            });

        var embeddingCache = new Mock<IMcpCapabilityEmbeddingCacheProvider>();
        embeddingCache
            .Setup(instance => instance.GetOrCreateEmbeddingsAsync(
                It.IsAny<IReadOnlyList<McpServerCapabilities>>(),
                It.IsAny<IEmbeddingGenerator<string, Embedding<float>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((
                IReadOnlyList<McpServerCapabilities> serverCapabilities,
                IEmbeddingGenerator<string, Embedding<float>> generator,
                CancellationToken cancellationToken) =>
                embeddingResolver is null
                    ? Task.FromResult(embeddings ?? (IReadOnlyList<McpCapabilityEmbeddingEntry>)[])
                    : embeddingResolver(serverCapabilities, generator, cancellationToken));

        var deploymentManager = new Mock<IAIDeploymentManager>();
        deploymentManager
            .Setup(instance => instance.ResolveOrDefaultAsync(
                AIDeploymentPurpose.Embedding,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(embeddingDeploymentAvailable
                ? new AIDeployment
                {
                    ItemId = "embedding",
                    Name = "embedding",
                    ClientName = "client",
                    Purpose = AIDeploymentPurpose.Embedding,
                }
                : null);

        var generator = new FixedEmbeddingGenerator([1f, 0f]);
        var clientFactory = new Mock<IAIClientFactory>();
        clientFactory
            .Setup(instance => instance.CreateEmbeddingGeneratorAsync(It.IsAny<AIDeployment>()))
            .Returns((AIDeployment deployment) =>
                factoryResolver is null
                    ? ValueTask.FromResult<IEmbeddingGenerator<string, Embedding<float>>>(generator)
                    : factoryResolver(deployment));

        var logger = new RecordingLogger<DefaultMcpCapabilityResolver>();
        var resolver = new DefaultMcpCapabilityResolver(
            store.Object,
            metadata.Object,
            embeddingCache.Object,
            clientFactory.Object,
            deploymentManager.Object,
            tokenizer ?? new LuceneTextTokenizer(),
            Options.Create(options ?? new McpCapabilityResolverOptions
            {
                IncludeAllThreshold = 20,
            }),
            logger);

        return new ResolverHarness(
            resolver,
            store,
            metadata,
            embeddingCache,
            clientFactory,
            deploymentManager,
            logger);
    }

    /// <summary>
    /// Creates an MCP connection.
    /// </summary>
    /// <param name="id">The connection identifier.</param>
    /// <returns>The connection.</returns>
    private static McpConnection CreateConnection(string id)
    {
        return new McpConnection
        {
            ItemId = id,
            DisplayText = id,
            Source = "test",
        };
    }

    /// <summary>
    /// Creates server capability metadata.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    /// <param name="tools">The tools.</param>
    /// <param name="prompts">The prompts.</param>
    /// <param name="resources">The resources.</param>
    /// <param name="resourceTemplates">The resource templates.</param>
    /// <returns>The server metadata.</returns>
    private static McpServerCapabilities CreateServer(
        string connectionId,
        IReadOnlyList<McpServerCapability> tools = null,
        IReadOnlyList<McpServerCapability> prompts = null,
        IReadOnlyList<McpServerCapability> resources = null,
        IReadOnlyList<McpServerCapability> resourceTemplates = null)
    {
        return new McpServerCapabilities
        {
            ConnectionId = connectionId,
            ConnectionDisplayText = connectionId,
            Tools = tools ?? [],
            Prompts = prompts ?? [],
            Resources = resources ?? [],
            ResourceTemplates = resourceTemplates ?? [],
            IsHealthy = true,
        };
    }

    /// <summary>
    /// Creates a server capability.
    /// </summary>
    /// <param name="name">The capability name.</param>
    /// <param name="description">The capability description.</param>
    /// <param name="uri">The resource URI.</param>
    /// <param name="uriTemplate">The resource URI template.</param>
    /// <returns>The capability.</returns>
    private static McpServerCapability CreateCapability(
        string name,
        string description = null,
        string uri = null,
        string uriTemplate = null)
    {
        return new McpServerCapability
        {
            Name = name,
            Description = description,
            Uri = uri,
            UriTemplate = uriTemplate,
        };
    }

    /// <summary>
    /// Creates a cached embedding entry with a score in the first vector component.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    /// <param name="name">The capability name.</param>
    /// <param name="type">The capability type.</param>
    /// <param name="score">The dot-product score.</param>
    /// <param name="description">The capability description.</param>
    /// <returns>The embedding entry.</returns>
    private static McpCapabilityEmbeddingEntry CreateEmbedding(
        string connectionId,
        string name,
        McpCapabilityType type,
        float score,
        string description = "")
    {
        return new McpCapabilityEmbeddingEntry
        {
            ConnectionId = connectionId,
            ConnectionDisplayText = connectionId,
            CapabilityName = name,
            CapabilityDescription = description,
            CapabilityType = type,
            Embedding = [score, 0f],
        };
    }

    /// <summary>
    /// Creates a token set with a controlled number of prompt overlaps.
    /// </summary>
    /// <param name="matchingCount">The number of tokens shared with the prompt.</param>
    /// <param name="totalCount">The total token count.</param>
    /// <param name="prefix">The prefix for non-matching tokens.</param>
    /// <returns>The token set.</returns>
    private static HashSet<string> CreateTokens(
        int matchingCount,
        int totalCount,
        string prefix)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < matchingCount; index++)
        {
            tokens.Add($"match-{index}");
        }

        for (var index = matchingCount; index < totalCount; index++)
        {
            tokens.Add($"{prefix}-{index}");
        }

        return tokens;
    }

    private sealed class ResolverHarness
    {
        /// <summary>
        /// Initializes a new resolver harness.
        /// </summary>
        /// <param name="resolver">The resolver.</param>
        /// <param name="store">The store mock.</param>
        /// <param name="metadata">The metadata mock.</param>
        /// <param name="embeddingCache">The embedding-cache mock.</param>
        /// <param name="clientFactory">The client-factory mock.</param>
        /// <param name="deploymentManager">The deployment-manager mock.</param>
        /// <param name="logger">The recording logger.</param>
        public ResolverHarness(
            DefaultMcpCapabilityResolver resolver,
            Mock<ISourceCatalog<McpConnection>> store,
            Mock<IMcpServerMetadataCacheProvider> metadata,
            Mock<IMcpCapabilityEmbeddingCacheProvider> embeddingCache,
            Mock<IAIClientFactory> clientFactory,
            Mock<IAIDeploymentManager> deploymentManager,
            RecordingLogger<DefaultMcpCapabilityResolver> logger)
        {
            Resolver = resolver;
            Store = store;
            Metadata = metadata;
            EmbeddingCache = embeddingCache;
            ClientFactory = clientFactory;
            DeploymentManager = deploymentManager;
            Logger = logger;
        }

        /// <summary>
        /// Gets the resolver.
        /// </summary>
        public DefaultMcpCapabilityResolver Resolver { get; }

        /// <summary>
        /// Gets the store mock.
        /// </summary>
        public Mock<ISourceCatalog<McpConnection>> Store { get; }

        /// <summary>
        /// Gets the metadata mock.
        /// </summary>
        public Mock<IMcpServerMetadataCacheProvider> Metadata { get; }

        /// <summary>
        /// Gets the embedding-cache mock.
        /// </summary>
        public Mock<IMcpCapabilityEmbeddingCacheProvider> EmbeddingCache { get; }

        /// <summary>
        /// Gets the client-factory mock.
        /// </summary>
        public Mock<IAIClientFactory> ClientFactory { get; }

        /// <summary>
        /// Gets the deployment-manager mock.
        /// </summary>
        public Mock<IAIDeploymentManager> DeploymentManager { get; }

        /// <summary>
        /// Gets the recording logger.
        /// </summary>
        public RecordingLogger<DefaultMcpCapabilityResolver> Logger { get; }
    }

    private sealed class MappedTextTokenizer : ITextTokenizer
    {
        private readonly Dictionary<string, int> _callCounts = new(StringComparer.Ordinal);
        private readonly IReadOnlyDictionary<string, HashSet<string>> _tokens;

        /// <summary>
        /// Initializes a tokenizer backed by exact text mappings.
        /// </summary>
        /// <param name="tokens">The token mappings.</param>
        public MappedTextTokenizer(IReadOnlyDictionary<string, HashSet<string>> tokens)
        {
            _tokens = tokens;
        }

        /// <summary>
        /// Returns the mapped tokens for the text.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns>The mapped tokens or an empty set.</returns>
        public HashSet<string> Tokenize(string text)
        {
            _callCounts.TryGetValue(text, out var count);
            _callCounts[text] = count + 1;

            return _tokens.TryGetValue(text, out var tokens)
                ? tokens
                : [];
        }

        /// <summary>
        /// Gets the number of calls recorded for exact text.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns>The call count.</returns>
        public int GetCallCount(string text)
        {
            return _callCounts.GetValueOrDefault(text);
        }
    }

    private sealed class FixedEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private readonly float[] _vector;

        /// <summary>
        /// Initializes a fixed embedding generator.
        /// </summary>
        /// <param name="vector">The generated vector.</param>
        public FixedEmbeddingGenerator(float[] vector)
        {
            _vector = vector;
        }

        /// <summary>
        /// Gets generator metadata.
        /// </summary>
        public EmbeddingGeneratorMetadata Metadata { get; } = new("fixed");

        /// <summary>
        /// Generates the fixed embedding for every input.
        /// </summary>
        /// <param name="values">The input values.</param>
        /// <param name="options">The generation options.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The generated embeddings.</returns>
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = new GeneratedEmbeddings<Embedding<float>>();

            foreach (var _ in values)
            {
                result.Add(new Embedding<float>(_vector));
            }

            return Task.FromResult(result);
        }

        /// <summary>
        /// Gets an optional service.
        /// </summary>
        /// <param name="serviceType">The service type.</param>
        /// <param name="serviceKey">The service key.</param>
        /// <returns>No service.</returns>
        public object GetService(Type serviceType, object serviceKey = null)
        {
            return null;
        }

        /// <summary>
        /// Disposes the generator.
        /// </summary>
        public void Dispose()
        {
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        /// <summary>
        /// Gets recorded log entries.
        /// </summary>
        public List<RecordedLogEntry> Entries { get; } = [];

        /// <summary>
        /// Begins a no-op logging scope.
        /// </summary>
        /// <typeparam name="TState">The scope state type.</typeparam>
        /// <param name="state">The scope state.</param>
        /// <returns>The no-op scope.</returns>
        public IDisposable BeginScope<TState>(TState state)
        {
            return EmptyScope.Instance;
        }

        /// <summary>
        /// Indicates that all log levels are enabled.
        /// </summary>
        /// <param name="logLevel">The log level.</param>
        /// <returns><see langword="true"/>.</returns>
        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        /// <summary>
        /// Records a formatted log entry.
        /// </summary>
        /// <typeparam name="TState">The state type.</typeparam>
        /// <param name="logLevel">The log level.</param>
        /// <param name="eventId">The event identifier.</param>
        /// <param name="state">The log state.</param>
        /// <param name="exception">The exception.</param>
        /// <param name="formatter">The formatter.</param>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            Entries.Add(new RecordedLogEntry(
                logLevel,
                formatter(state, exception),
                exception));
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        /// <summary>
        /// Gets the shared empty scope.
        /// </summary>
        public static EmptyScope Instance { get; } = new();

        /// <summary>
        /// Disposes the scope.
        /// </summary>
        public void Dispose()
        {
        }
    }

    private sealed record RecordedLogEntry(
        LogLevel Level,
        string Message,
        Exception Exception);
}
