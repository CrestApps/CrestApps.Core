using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CrestApps.Core.AI.Tooling.Instances.Documentation;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Tests.Core.Tools;

/// <summary>
/// Covers which published index formats the search-index source understands, and what it says when it
/// cannot read one.
/// </summary>
/// <remarks>
/// "A prebuilt JSON search index" is not one format, so the shape is detected rather than configured. When
/// detection fails the corpus is empty, and an empty corpus reaches the caller as "no results were found" —
/// the same words a site with no matching page produces. A misconfigured index URL, an index published by a
/// generator that is not supported, and a genuinely empty site are indistinguishable from the outside, so
/// the only way to tell them apart is the log.
/// </remarks>
public sealed class SearchIndexDocumentationDiagnosticsTests
{
    private const string IndexUrl = "https://docs.test/search/search_index.json";

    /// <summary>
    /// Verifies that a MkDocs index is read, and that a healthy read stays quiet.
    /// </summary>
    /// <remarks>
    /// A diagnostic that fires on success is noise, so silence on the happy path is part of the contract.
    /// </remarks>
    [Fact]
    public async Task MkDocsIndex_IsRead()
    {
        var logger = new CapturingLogger();
        var source = CreateSource(logger, """
            {"config":{},"docs":[{"location":"reference/content-types/","title":"Content Types","text":"A content type is composed of content parts."}]}
            """);

        var results = await SearchAsync(source, "content type");

        Assert.Single(results);
        Assert.Equal("Content Types", results[0].Title);
        Assert.Equal("https://docs.test/reference/content-types/", results[0].Url);
        Assert.Empty(logger.Warnings);
    }

    /// <summary>
    /// Verifies that a Lunr/Docusaurus index is read, which is an array of blocks rather than an object.
    /// </summary>
    /// <remarks>
    /// This shape used to fail before it was even inspected: deserializing an array into the MkDocs type threw
    /// at the first character. A page-title entry names itself in <c>t</c> and carries breadcrumbs, so the
    /// last breadcrumb is the page it belongs to.
    /// </remarks>
    [Fact]
    public async Task LunrIndex_IsRead()
    {
        var logger = new CapturingLogger();
        var source = CreateSource(logger, """
            [{"documents":[{"i":1,"t":"Artificial Intelligence Suite","u":"/docs/ai/","b":["Docs","Modules"]}],"index":{"version":"2.3.9"}}]
            """);

        var results = await SearchAsync(source, "artificial intelligence");

        Assert.Single(results);
        Assert.Equal("Modules", results[0].Title);
        Assert.Equal("https://docs.test/docs/ai/", results[0].Url);
        Assert.Empty(logger.Warnings);
    }

    /// <summary>
    /// Verifies that a Lunr passage entry is titled by its page and linked to its heading.
    /// </summary>
    /// <remarks>
    /// The generator emits several kinds of block and they all use <c>t</c>, so for a passage that field is
    /// prose rather than a name. Titling such an entry with <c>t</c> puts a whole paragraph where a page name
    /// belongs, and dropping <c>h</c> cites the top of the page instead of the section that answered.
    /// </remarks>
    [Fact]
    public async Task LunrPassageEntry_IsTitledByItsPageAndLinkedToItsHeading()
    {
        var logger = new CapturingLogger();
        var source = CreateSource(logger, """
            [{"documents":[{"i":2,"t":"Provider connections describe how the site reaches a model.","u":"/docs/ai/overview","h":"#provider-connections","s":"AI Overview","p":1}],"index":{}}]
            """);

        var results = await SearchAsync(source, "provider connections");

        Assert.Single(results);
        Assert.Equal("AI Overview", results[0].Title);
        Assert.Equal("https://docs.test/docs/ai/overview#provider-connections", results[0].Url);
    }

    /// <summary>
    /// Verifies that a rooted location is resolved against the base URL, and an http one is left alone.
    /// </summary>
    /// <remarks>
    /// Asking <see cref="Uri.TryCreate(string, UriKind, out Uri)"/> whether a location is absolute is not
    /// enough: on Unix a rooted path parses as an absolute file URI, so "/docs/ai/" is handed back unchanged
    /// there while being joined to the base URL on Windows. MkDocs states its locations relative and never
    /// showed this; a Lunr index states them rooted, so the same index produced different links per platform.
    /// </remarks>
    [Theory]
    [InlineData("/docs/ai/", "https://docs.test/docs/ai/")]
    [InlineData("docs/ai/", "https://docs.test/docs/ai/")]
    [InlineData("https://elsewhere.test/docs/ai/", "https://elsewhere.test/docs/ai/")]
    public async Task Location_IsResolvedAgainstTheBaseUrlUnlessItIsAlreadyHttp(string location, string expected)
    {
        var source = CreateSource(new CapturingLogger(), $$"""
            {"docs":[{"location":"{{location}}","title":"Alpha","text":"content types are defined here"}]}
            """);

        var results = await SearchAsync(source, "content types");

        Assert.Single(results);
        Assert.Equal(expected, results[0].Url);
    }

    /// <summary>
    /// Verifies that an index from an unsupported generator is reported rather than read as an empty site.
    /// </summary>
    /// <remarks>
    /// This is the case that used to pass silently: the payload is valid JSON and deserialized without error,
    /// it simply carried its entries under a name the reader does not know, so nothing threw and nothing was
    /// logged.
    /// </remarks>
    [Fact]
    public async Task UnrecognizedIndexFormat_IsReported()
    {
        var logger = new CapturingLogger();
        var source = CreateSource(logger, """{"site":"gallery","count":2,"items":[{"title":"A"},{"title":"B"}]}""");

        Assert.Empty(await SearchAsync(source, "anything"));
        Assert.Contains(logger.Warnings, w => w.Contains("not in a recognized format", StringComparison.Ordinal) && w.Contains(IndexUrl, StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that a recognized index carrying nothing usable is reported.
    /// </summary>
    [Fact]
    public async Task RecognizedIndexWithNoUsableEntries_IsReported()
    {
        var logger = new CapturingLogger();
        var source = CreateSource(logger, """{"docs":[{"title":"A"},{"title":"B"}]}""");

        Assert.Empty(await SearchAsync(source, "anything"));
        Assert.Contains(logger.Warnings, w => w.Contains("no entry carried", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that an unreachable index is reported.
    /// </summary>
    [Fact]
    public async Task UnreachableIndex_IsReported()
    {
        var logger = new CapturingLogger();
        var source = CreateSource(logger, body: null);

        Assert.Empty(await SearchAsync(source, "anything"));
        Assert.Contains(logger.Warnings, w => w.Contains("Failed to read search index", StringComparison.Ordinal));
    }

    private static async Task<IReadOnlyList<DocumentationSearchResult>> SearchAsync(SearchIndexDocumentationSource source, string query)
        => await source.SearchAsync(new DocumentationSearchRequest(query) { MaxResults = 5 }, TestContext.Current.CancellationToken);

    private static SearchIndexDocumentationSource CreateSource(ILogger logger, string body)
    {
        var site = new DocumentationSearchIndexSite
        {
            Name = "docs",
            BaseUrl = "https://docs.test/",
            IndexUrl = IndexUrl,
        };

        return new SearchIndexDocumentationSource(
            site,
            new DocumentationSearchOptions(),
            new StubHttpClientFactory(body),
            TimeProvider.System,
            logger);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly string _body;

        public StubHttpClientFactory(string body)
        {
            _body = body;
        }

        public HttpClient CreateClient(string name)
            => new(new StubHandler(_body), disposeHandler: true);

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly string _body;

            public StubHandler(string body)
            {
                _body = body;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (_body is null)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                }

                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes(_body)),
                };

                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                return Task.FromResult(response);
            }
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
