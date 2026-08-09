using CrestApps.Core.AI.Mcp.Documentation;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.Tests.Core.Mcp;

public sealed class DocumentationSearchTests
{
    /// <summary>
    /// Verifies that the sitemap source materializes a documentation search function whose model-facing
    /// name and description are derived from the configured instance.
    /// </summary>
    [Fact]
    public void SitemapSource_CreateTool_DerivesNameAndDescriptionFromInstance()
    {
        var instance = new AIToolInstance
        {
            ItemId = "instance-1",
            Source = DocumentationToolConstants.SitemapSourceName,
            Name = "crestapps-docs",
            Description = "Searches the CrestApps documentation.",
        };

        instance.Put(new SitemapDocumentationToolSettings
        {
            BaseUrl = "https://core.crestapps.com",
        });

        var source = new SitemapDocumentationToolSource();
        var tool = source.CreateTool(instance);

        var function = Assert.IsType<DocumentationSearchToolFunction>(tool);

        Assert.Equal(instance.GetFunctionName(), function.Name);
        Assert.Equal("Searches the CrestApps documentation.", function.Description);
    }

    /// <summary>
    /// Verifies that the search-index source produces a documentation search function.
    /// </summary>
    [Fact]
    public void SearchIndexSource_CreateTool_ProducesFunction()
    {
        var instance = new AIToolInstance
        {
            ItemId = "instance-2",
            Source = DocumentationToolConstants.SearchIndexSourceName,
            Name = "mkdocs",
        };

        instance.Put(new SearchIndexDocumentationToolSettings
        {
            BaseUrl = "https://docs.example.com",
            IndexUrl = "https://docs.example.com/search/search_index.json",
        });

        var source = new SearchIndexDocumentationToolSource();
        var tool = source.CreateTool(instance);

        Assert.IsType<DocumentationSearchToolFunction>(tool);
    }

    /// <summary>
    /// Verifies that the Algolia source produces a documentation search function.
    /// </summary>
    [Fact]
    public void AlgoliaSource_CreateTool_ProducesFunction()
    {
        var instance = new AIToolInstance
        {
            ItemId = "instance-3",
            Source = DocumentationToolConstants.AlgoliaSourceName,
            Name = "algolia",
        };

        instance.Put(new AlgoliaDocumentationToolSettings
        {
            ApplicationId = "APP123",
            ApiKey = "search-key",
            IndexName = "docs-index",
        });

        var source = new AlgoliaDocumentationToolSource();
        var tool = source.CreateTool(instance);

        Assert.IsType<DocumentationSearchToolFunction>(tool);
    }

    /// <summary>
    /// Verifies that invoking the function returns a helpful message when the required query is missing.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenQueryMissing_ReturnsMessage()
    {
        var function = CreateFunction(new FakeDocumentationSource("docs"));

        var result = await InvokeAsync(function, query: null);

        Assert.Contains("query", result, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that invoking the function returns a message when the source yields no results.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_WhenNoResults_ReturnsMessage()
    {
        var function = CreateFunction(new FakeDocumentationSource("docs"));

        var result = await InvokeAsync(function, "anything");

        Assert.Equal("No documentation results were found for 'anything'.", result);
    }

    /// <summary>
    /// Verifies that invoking the function formats the results returned by the bound source.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_FormatsResults()
    {
        var source = new FakeDocumentationSource(
            "docs",
            new DocumentationSearchResult
            {
                SourceName = "docs",
                Title = "Getting started",
                Url = "https://core.crestapps.com/start",
                Snippet = "How to begin.",
                Score = 5,
            });

        var function = CreateFunction(source);

        var result = await InvokeAsync(function, "start");

        Assert.Contains("[1] Getting started — https://core.crestapps.com/start", result);
        Assert.Contains("How to begin.", result);
    }

    /// <summary>
    /// Verifies that the materializer caches the built source until the signature changes.
    /// </summary>
    [Fact]
    public void Materializer_CachesUntilSignatureChanges()
    {
        var materializer = new DefaultDocumentationSourceMaterializer();
        var buildCount = 0;

        IDocumentationSource Factory()
        {
            buildCount++;

            return new FakeDocumentationSource("docs");
        }

        var first = materializer.GetOrCreate("key", "sig-1", Factory);
        var second = materializer.GetOrCreate("key", "sig-1", Factory);

        Assert.Same(first, second);
        Assert.Equal(1, buildCount);

        var third = materializer.GetOrCreate("key", "sig-2", Factory);

        Assert.NotSame(first, third);
        Assert.Equal(2, buildCount);
    }

    private static DocumentationSearchToolFunction CreateFunction(IDocumentationSource source)
    {
        var instance = new AIToolInstance
        {
            ItemId = "instance-1",
            Name = "docs",
            CreatedUtc = DateTime.UnixEpoch,
        };

        return new DocumentationSearchToolFunction("docs", "Docs search", instance, _ => source);
    }

    private static async Task<string> InvokeAsync(DocumentationSearchToolFunction function, string query)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDocumentationSourceMaterializer, DefaultDocumentationSourceMaterializer>();

        using var provider = services.BuildServiceProvider();

        var arguments = new AIFunctionArguments
        {
            Services = provider,
        };

        if (query is not null)
        {
            arguments["query"] = query;
        }

        var result = await function.InvokeAsync(arguments, TestContext.Current.CancellationToken);

        return result?.ToString();
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
