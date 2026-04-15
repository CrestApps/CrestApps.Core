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
        return $"/Articles/{referenceId}";
    }
}
