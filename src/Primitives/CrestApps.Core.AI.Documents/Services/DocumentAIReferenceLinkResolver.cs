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

    public DocumentAIReferenceLinkResolver(
        LinkGenerator linkGenerator,
        IHttpContextAccessor httpContextAccessor)
    {
        _linkGenerator = linkGenerator;
        _httpContextAccessor = httpContextAccessor;
    }

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
