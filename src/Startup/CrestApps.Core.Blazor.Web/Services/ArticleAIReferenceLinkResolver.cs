using CrestApps.Core.AI.Profiles;

namespace CrestApps.Core.Blazor.Web.Services;

public sealed class ArticleAIReferenceLinkResolver : IAIReferenceLinkResolver
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ArticleAIReferenceLinkResolver(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string ResolveLink(string referenceId, IDictionary<string, object> metadata)
    {
        if (string.IsNullOrWhiteSpace(referenceId))
        {
            return null;
        }

        var request = _httpContextAccessor.HttpContext?.Request;
        if (request is null)
        {
            return $"/Articles/{referenceId}";
        }

        return $"/Articles/{referenceId}";
    }
}
