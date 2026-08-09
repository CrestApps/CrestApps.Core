using CrestApps.Core.AI.Mcp;
using CrestApps.Core.AI.Mcp.Documentation;
using CrestApps.Core.AI.Mcp.Functions;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Mcp;

public sealed class DocumentationSearchTests
{
    /// <summary>
    /// Verifies that the registration extension registers the documentation search tool with the
    /// expected category and purpose so it can be exposed and filtered by a knowledge-base MCP server.
    /// </summary>
    [Fact]
    public void AddCoreAIDocumentationSearch_RegistersToolWithCategoryAndPurpose()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCoreAIDocumentationSearch();

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<AIToolDefinitionOptions>>().Value;

        Assert.True(options.Tools.TryGetValue(DocumentationSearchFunction.TheName, out var entry));
        Assert.Equal(DocumentationSearchFunction.Category, entry.Category);
        Assert.Equal(AIToolPurposes.DataSourceSearch, entry.Purpose);
        Assert.NotNull(provider.GetRequiredService<DocumentationSearchFunction>());
        Assert.NotNull(provider.GetRequiredService<IDocumentationSourceProvider>());
    }

    /// <summary>
    /// Verifies that <see cref="DocumentationSearchBuilder.AddSite"/> records the site in the options.
    /// </summary>
    [Fact]
    public void AddSite_PopulatesOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCoreAIDocumentationSearch(docs => docs
            .AddSite("crestapps", "https://core.crestapps.com", site => site.MaxResults = 3));

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<DocumentationSearchOptions>>().Value;
        var site = Assert.Single(options.Sites);

        Assert.Equal("crestapps", site.Name);
        Assert.Equal("https://core.crestapps.com", site.BaseUrl);
        Assert.Equal(3, site.MaxResults);
    }

    /// <summary>
    /// Verifies that <see cref="DocumentationSearchBuilder.AddSearchIndex"/> records the search-index
    /// site in the options.
    /// </summary>
    [Fact]
    public void AddSearchIndex_PopulatesOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCoreAIDocumentationSearch(docs => docs
            .AddSearchIndex("mkdocs", "https://docs.example.com", site =>
            {
                site.IndexUrl = "https://docs.example.com/search/search_index.json";
                site.MaxResults = 4;
            }));

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<DocumentationSearchOptions>>().Value;
        var site = Assert.Single(options.SearchIndexes);

        Assert.Equal("mkdocs", site.Name);
        Assert.Equal("https://docs.example.com", site.BaseUrl);
        Assert.Equal("https://docs.example.com/search/search_index.json", site.IndexUrl);
        Assert.Equal(4, site.MaxResults);
    }

    /// <summary>
    /// Verifies that <see cref="DocumentationSearchBuilder.AddAlgoliaDocSearch"/> records the Algolia
    /// site in the options.
    /// </summary>
    [Fact]
    public void AddAlgoliaDocSearch_PopulatesOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCoreAIDocumentationSearch(docs => docs
            .AddAlgoliaDocSearch("algolia", "APP123", "search-key", "docs-index", site => site.MaxResults = 6));

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<DocumentationSearchOptions>>().Value;
        var site = Assert.Single(options.AlgoliaSources);

        Assert.Equal("algolia", site.Name);
        Assert.Equal("APP123", site.ApplicationId);
        Assert.Equal("search-key", site.ApiKey);
        Assert.Equal("docs-index", site.IndexName);
        Assert.Equal(6, site.MaxResults);
    }

    /// <summary>
    /// Verifies that the source provider aggregates code-registered custom sources with the built-in
    /// crawler sources materialized from the configured sites.
    /// </summary>
    [Fact]
    public async Task SourceProvider_AggregatesCustomAndConfiguredSites()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCoreAIDocumentationSearch(docs => docs
            .AddSite("site-1", "https://docs.example.com")
            .AddSearchIndex("index-1", "https://mkdocs.example.com")
            .AddAlgoliaDocSearch("algolia-1", "APP123", "search-key", "docs-index")
            .AddSource(new FakeDocumentationSource("custom-1")));

        using var provider = services.BuildServiceProvider();

        var sources = await provider.GetRequiredService<IDocumentationSourceProvider>()
            .GetSourcesAsync(provider, TestContext.Current.CancellationToken);

        Assert.Contains(sources, source => source.Name == "custom-1");
        Assert.Contains(sources, source => source.Name == "site-1");
        Assert.Contains(sources, source => source.Name == "index-1");
        Assert.Contains(sources, source => source.Name == "algolia-1");
    }

    /// <summary>
    /// Verifies that the source provider materializes documentation sources stored in the catalog (for
    /// example a database-backed store) through the registered strategy factories.
    /// </summary>
    [Fact]
    public async Task SourceProvider_MaterializesCatalogEntries()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCoreAIDocumentationSearch();
        services.AddScoped<INamedSourceCatalogSource<DocumentationSourceEntry>>(_ => new FakeCatalogSource(
            new DocumentationSourceEntry
            {
                ItemId = "01HZZZDBSOURCE0000000000001",
                Name = "stored-site",
                Source = DocumentationSourceStrategies.Sitemap,
                BaseUrl = "https://stored.example.com",
            }));

        using var root = services.BuildServiceProvider();
        using var scope = root.CreateScope();

        var sources = await scope.ServiceProvider.GetRequiredService<IDocumentationSourceProvider>()
            .GetSourcesAsync(scope.ServiceProvider, TestContext.Current.CancellationToken);

        Assert.Contains(sources, source => source.Name == "stored-site");
    }

    /// <summary>
    /// Verifies that the tool returns a helpful message when no documentation sources are configured.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenNoSourcesConfigured_ReturnsMessage()
    {
        using var provider = BuildProvider();

        var result = await InvokeAsync(provider, "anything");

        Assert.Equal("No documentation sources are configured.", result);
    }

    /// <summary>
    /// Verifies that the tool aggregates results across sources and orders them by descending score.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_AggregatesResultsOrderedByScore()
    {
        var low = new FakeDocumentationSource("source-a", new DocumentationSearchResult
        {
            SourceName = "source-a",
            Title = "Low",
            Url = "https://a/low",
            Snippet = "low snippet",
            Score = 1,
        });

        var high = new FakeDocumentationSource("source-b", new DocumentationSearchResult
        {
            SourceName = "source-b",
            Title = "High",
            Url = "https://b/high",
            Snippet = "high snippet",
            Score = 9,
        });

        using var provider = BuildProvider(low, high);

        var result = await InvokeAsync(provider, "topic");

        Assert.Contains("[1] High — https://b/high", result);
        Assert.Contains("[2] Low — https://a/low", result);
        Assert.True(result.IndexOf("High", StringComparison.Ordinal) < result.IndexOf("Low", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that supplying an unknown source name returns a message instead of silently searching all.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WithUnknownSource_ReturnsMessage()
    {
        using var provider = BuildProvider(new FakeDocumentationSource("known"));

        var result = await InvokeAsync(provider, "topic", "missing");

        Assert.Equal("No documentation source named 'missing' is configured.", result);
    }

    /// <summary>
    /// Verifies that supplying a source name scopes the search to that single source.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WithSourceName_ScopesToNamedSource()
    {
        var wanted = new FakeDocumentationSource("wanted", new DocumentationSearchResult
        {
            SourceName = "wanted",
            Title = "Wanted",
            Url = "https://wanted/doc",
            Snippet = "wanted snippet",
            Score = 5,
        });

        var other = new FakeDocumentationSource("other", new DocumentationSearchResult
        {
            SourceName = "other",
            Title = "Other",
            Url = "https://other/doc",
            Snippet = "other snippet",
            Score = 8,
        });

        using var provider = BuildProvider(wanted, other);

        var result = await InvokeAsync(provider, "topic", "wanted");

        Assert.Contains("Wanted", result);
        Assert.DoesNotContain("Other", result);
    }

    private static ServiceProvider BuildProvider(params IDocumentationSource[] sources)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDocumentationSourceProvider>(new StubSourceProvider(sources));

        return services.BuildServiceProvider();
    }

    private static async Task<string> InvokeAsync(IServiceProvider provider, string query, string source = null)
    {
        var function = new DocumentationSearchFunction();
        var arguments = new AIFunctionArguments
        {
            Services = provider,
        };

        arguments["query"] = query;

        if (source is not null)
        {
            arguments["source"] = source;
        }

        var result = await function.InvokeAsync(arguments, TestContext.Current.CancellationToken);

        return result?.ToString();
    }

    private sealed class StubSourceProvider : IDocumentationSourceProvider
    {
        private readonly IReadOnlyList<IDocumentationSource> _sources;

        public StubSourceProvider(IReadOnlyList<IDocumentationSource> sources)
        {
            _sources = sources;
        }

        public ValueTask<IReadOnlyList<IDocumentationSource>> GetSourcesAsync(IServiceProvider services, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(_sources);
        }
    }

    private sealed class FakeCatalogSource : INamedSourceCatalogSource<DocumentationSourceEntry>
    {
        private readonly IReadOnlyCollection<DocumentationSourceEntry> _entries;

        public FakeCatalogSource(params DocumentationSourceEntry[] entries)
        {
            _entries = entries;
        }

        public int Order => 0;

        public ValueTask<IReadOnlyCollection<DocumentationSourceEntry>> GetEntriesAsync(IReadOnlyCollection<DocumentationSourceEntry> knownEntries, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(_entries);
        }
    }

    private sealed class FakeDocumentationSource : IDocumentationSource
    {
        private readonly DocumentationSearchResult[] _results;

        public FakeDocumentationSource(string name, params DocumentationSearchResult[] results)
        {
            Name = name;
            _results = results;
        }

        public string Name { get; }

        public Task<IReadOnlyList<DocumentationSearchResult>> SearchAsync(DocumentationSearchRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DocumentationSearchResult>>(_results);
        }
    }
}
