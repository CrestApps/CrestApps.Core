using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CrestApps.Core.AI.Tooling.Instances.Documentation;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Tests.Core.Tools;

/// <summary>
/// Covers what the search-index source says when it cannot read an index.
/// </summary>
/// <remarks>
/// Every failure here ends in an empty corpus, and an empty corpus reaches the caller as "no results were
/// found" — the same words a site with no matching page produces. A misconfigured index URL, an index
/// published in another tool's format and a genuinely empty site are indistinguishable from the outside,
/// so the only way to tell them apart is the log. These assert that the log actually says which happened.
/// </remarks>
public sealed class SearchIndexDocumentationDiagnosticsTests
{
    private const string IndexUrl = "https://docs.test/search/search_index.json";

    /// <summary>
    /// Verifies that an index in another tool's format is reported, rather than read as an empty site.
    /// </summary>
    /// <remarks>
    /// This is the case that used to pass silently: the payload is valid JSON and deserializes, it simply
    /// carries its entries under a different name, so nothing threw and nothing was logged.
    /// </remarks>
    [Fact]
    public async Task IndexWithoutADocsArray_IsReportedAsNotASearchIndex()
    {
        var logger = new CapturingLogger();
        var source = CreateSource(logger, """{"site":"gallery","count":2,"items":[{"title":"A"},{"title":"B"}]}""");

        var results = await source.SearchAsync(new DocumentationSearchRequest("anything") { MaxResults = 5 }, TestContext.Current.CancellationToken);

        Assert.Empty(results);
        Assert.Contains(logger.Warnings, w => w.Contains("no 'docs' array", StringComparison.Ordinal) && w.Contains(IndexUrl, StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that an index holding no documents is reported.
    /// </summary>
    [Fact]
    public async Task IndexWithNoDocuments_IsReported()
    {
        var logger = new CapturingLogger();
        var source = CreateSource(logger, """{"docs":[]}""");

        var results = await source.SearchAsync(new DocumentationSearchRequest("anything") { MaxResults = 5 }, TestContext.Current.CancellationToken);

        Assert.Empty(results);
        Assert.Contains(logger.Warnings, w => w.Contains("contains no documents", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that documents missing the fields the search reads are reported rather than silently dropped.
    /// </summary>
    [Fact]
    public async Task DocumentsWithoutLocationOrText_AreReported()
    {
        var logger = new CapturingLogger();
        var source = CreateSource(logger, """{"docs":[{"title":"A"},{"title":"B"}]}""");

        var results = await source.SearchAsync(new DocumentationSearchRequest("anything") { MaxResults = 5 }, TestContext.Current.CancellationToken);

        Assert.Empty(results);
        Assert.Contains(logger.Warnings, w => w.Contains("none carry both a location and text", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that an unreachable index is still reported, and that a readable one stays quiet.
    /// </summary>
    [Fact]
    public async Task UnreachableIndexWarns_AndAReadableOneDoesNot()
    {
        var missing = new CapturingLogger();
        var missingSource = CreateSource(missing, body: null);

        Assert.Empty(await missingSource.SearchAsync(new DocumentationSearchRequest("anything") { MaxResults = 5 }, TestContext.Current.CancellationToken));
        Assert.NotEmpty(missing.Warnings);

        var healthy = new CapturingLogger();
        var healthySource = CreateSource(healthy, """{"docs":[{"location":"a/","title":"Alpha","text":"content types are defined here"}]}""");

        var results = await healthySource.SearchAsync(new DocumentationSearchRequest("content types") { MaxResults = 5 }, TestContext.Current.CancellationToken);

        Assert.NotEmpty(results);
        Assert.Empty(healthy.Warnings);
    }

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
