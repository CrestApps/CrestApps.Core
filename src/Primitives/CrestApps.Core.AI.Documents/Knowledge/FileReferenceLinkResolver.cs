using CrestApps.Core.AI.Documents.Endpoints;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CrestApps.Core.AI.Documents.Knowledge;

/// <summary>
/// Resolves citation links for <c>Ingested</c> data source references. A figure or a chart links to its
/// stored picture; everything else has nothing to download, so it has no link.
/// </summary>
public sealed class FileReferenceLinkResolver : IAIReferenceLinkResolver
{
    private readonly LinkGenerator _linkGenerator;
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileReferenceLinkResolver"/> class.
    /// </summary>
    /// <param name="linkGenerator">The link generator.</param>
    /// <param name="httpContextAccessor">The http context accessor.</param>
    public FileReferenceLinkResolver(
        LinkGenerator linkGenerator,
        IHttpContextAccessor httpContextAccessor)
    {
        _linkGenerator = linkGenerator;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Resolves the citation link for one knowledge object.
    /// </summary>
    /// <param name="referenceId">The reference id, which is the object's canonical identifier.</param>
    /// <param name="metadata">The reference metadata, which carries the owning data source identifier.</param>
    /// <returns>The figure download path, or <see langword="null"/> when the object is not a picture.</returns>
    public string ResolveLink(string referenceId, IDictionary<string, object> metadata)
    {
        if (string.IsNullOrWhiteSpace(referenceId))
        {
            return null;
        }

        if (!referenceId.StartsWith(KnowledgeObjectTypes.Figure + ':', StringComparison.Ordinal) &&
            !referenceId.StartsWith(KnowledgeObjectTypes.Chart + ':', StringComparison.Ordinal))
        {
            return null;
        }

        if (metadata is null ||
            !metadata.TryGetValue("DataSourceId", out var raw) ||
            raw is not string dataSourceId ||
            string.IsNullOrWhiteSpace(dataSourceId))
        {
            return null;
        }

        var values = new RouteValueDictionary
        {
            ["dataSourceId"] = dataSourceId,
            ["canonicalId"] = referenceId,
        };

        var httpContext = _httpContextAccessor.HttpContext;

        // Citations are also collected outside a request, by background work that finishes a conversation.
        // There is no host or path base to apply then, so the link is built without one rather than not at
        // all.
        if (httpContext is null)
        {
            return _linkGenerator.GetPathByName(DownloadKnowledgeFigure.DefaultRouteName, values);
        }

        // Absolute, on the host serving the request. A bare path is correct and renders perfectly well in a
        // browser, but this address is handed to a language model and asked to be embedded as an image, and
        // a model shown a path with no scheme or host reliably replaces it with an absolute URL it invents —
        // which renders as a broken image that reads like a citation. Naming the host removes the ambiguity
        // that invites the invention.
        return _linkGenerator.GetUriByName(httpContext, DownloadKnowledgeFigure.DefaultRouteName, values)
            ?? _linkGenerator.GetPathByName(httpContext, DownloadKnowledgeFigure.DefaultRouteName, values);
    }
}
