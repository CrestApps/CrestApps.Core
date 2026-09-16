using CrestApps.Core.AI.Documents.Knowledge;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CrestApps.Core.Tests.Core.Documents.Knowledge;

public sealed class FileReferenceLinkResolverTests
{
    /// <summary>
    /// Verifies that a figure cited in an answer links to its picture, and that a chunk of text - which has
    /// no picture to serve - gets no link rather than a broken one.
    /// </summary>
    [Fact]
    public void ResolveLink_FigureIdYieldsRoute_TextIdYieldsNull()
    {
        var resolver = CreateResolver(out var linkGenerator);

        var metadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["DataSourceId"] = "data-source-1",
        };

        Assert.NotNull(resolver.ResolveLink("figure:0123456789abcdef:1:2", metadata));
        Assert.Equal("DownloadKnowledgeFigure", linkGenerator.LastRouteName);
        Assert.Equal("data-source-1", linkGenerator.LastValues["dataSourceId"]);
        Assert.Equal("figure:0123456789abcdef:1:2", linkGenerator.LastValues["canonicalId"]);

        Assert.NotNull(resolver.ResolveLink("chart:0123456789abcdef:1:3", metadata));

        Assert.Null(resolver.ResolveLink("text:0123456789abcdef:1:0", metadata));
        Assert.Null(resolver.ResolveLink("article:0123456789abcdef:1", metadata));
    }

    /// <summary>
    /// Verifies that a figure whose data source is unknown gets no link, because the endpoint is scoped to a
    /// data source and a guessed one would serve the wrong bytes or none.
    /// </summary>
    [Fact]
    public void ResolveLink_WithoutDataSourceId_ReturnsNull()
    {
        var resolver = CreateResolver(out _);

        Assert.Null(resolver.ResolveLink("figure:0123456789abcdef:1:2", null));
        Assert.Null(resolver.ResolveLink("figure:0123456789abcdef:1:2", new Dictionary<string, object>()));
    }

    private static FileReferenceLinkResolver CreateResolver(out RecordingLinkGenerator linkGenerator)
    {
        linkGenerator = new RecordingLinkGenerator();

        return new FileReferenceLinkResolver(linkGenerator, new HttpContextAccessor());
    }

    /// <summary>
    /// Records the route name and values a resolver asked for, and answers with a path built from them.
    /// </summary>
    private sealed class RecordingLinkGenerator : LinkGenerator
    {
        public string LastRouteName { get; private set; }

        public RouteValueDictionary LastValues { get; private set; }

        public override string GetPathByAddress<TAddress>(
            HttpContext httpContext,
            TAddress address,
            RouteValueDictionary values,
            RouteValueDictionary ambientValues = null,
            PathString? pathBase = null,
            FragmentString fragment = default,
            LinkOptions options = null)
        {
            return Record(address, values);
        }

        public override string GetPathByAddress<TAddress>(
            TAddress address,
            RouteValueDictionary values,
            PathString pathBase = default,
            FragmentString fragment = default,
            LinkOptions options = null)
        {
            return Record(address, values);
        }

        public override string GetUriByAddress<TAddress>(
            HttpContext httpContext,
            TAddress address,
            RouteValueDictionary values,
            RouteValueDictionary ambientValues = null,
            string scheme = null,
            HostString? host = null,
            PathString? pathBase = null,
            FragmentString fragment = default,
            LinkOptions options = null)
        {
            return Record(address, values);
        }

        public override string GetUriByAddress<TAddress>(
            TAddress address,
            RouteValueDictionary values,
            string scheme,
            HostString host,
            PathString pathBase = default,
            FragmentString fragment = default,
            LinkOptions options = null)
        {
            return Record(address, values);
        }

        private string Record<TAddress>(TAddress address, RouteValueDictionary values)
        {
            LastRouteName = address as string ?? (address as RouteValuesAddress)?.RouteName;
            LastValues = values;

            return $"/{LastRouteName}";
        }
    }
}
