using CrestApps.Core.AI.Documents.Endpoints;
using CrestApps.Core.AI.Profiles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CrestApps.Core.AI.Documents.Services;

/// <summary>
/// Resolves citation links for stored AI documents to the shared download endpoint.
/// </summary>
public sealed class DocumentAIReferenceLinkResolver : IAIReferenceLinkResolver
{
    private readonly LinkGenerator _linkGenerator;
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentAIReferenceLinkResolver"/> class.
    /// </summary>
    /// <param name="linkGenerator">The link generator.</param>
    /// <param name="httpContextAccessor">The http context accessor.</param>
    public DocumentAIReferenceLinkResolver(
        LinkGenerator linkGenerator,
        IHttpContextAccessor httpContextAccessor)
    {
        _linkGenerator = linkGenerator;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Resolves link.
    /// </summary>
    /// <param name="referenceId">The reference id.</param>
    /// <param name="metadata">The metadata.</param>
    public string ResolveLink(string referenceId, IDictionary<string, object> metadata)
    {
        if (string.IsNullOrWhiteSpace(referenceId))
        {
            return null;
        }

        return _linkGenerator.GetPathByName(
            _httpContextAccessor.HttpContext,
            DownloadAIDocument.DefaultRouteName,
            new RouteValueDictionary
            {
                ["documentId"] = referenceId,
            });
    }
}
