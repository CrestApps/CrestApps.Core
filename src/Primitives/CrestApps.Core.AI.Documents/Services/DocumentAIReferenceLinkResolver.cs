using CrestApps.Core.AI.Documents.Endpoints;
using CrestApps.Core.AI.Profiles;
using Microsoft.AspNetCore.Http;

namespace CrestApps.Core.AI.Documents.Services;

/// <summary>
/// Resolves citation links for stored AI documents to the shared download endpoint.
/// </summary>
public sealed class DocumentAIReferenceLinkResolver : IAIReferenceLinkResolver
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentAIReferenceLinkResolver"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">The http context accessor.</param>
    public DocumentAIReferenceLinkResolver(IHttpContextAccessor httpContextAccessor)
    {
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

        // Build the path directly from the static route pattern instead of resolving by endpoint
        // name. Name-based link generation forces ASP.NET Core to validate that every endpoint name
        // is globally unique, which throws in multi-tenant hosts where other modules register the
        // same named API endpoints across tenants.
        var relativePath = "/" + DownloadAIDocument.RoutePattern.Replace(
            "{documentId}",
            Uri.EscapeDataString(referenceId),
            StringComparison.Ordinal);

        var pathBase = _httpContextAccessor.HttpContext?.Request.PathBase ?? PathString.Empty;

        return pathBase.Add(relativePath).Value;
    }
}
