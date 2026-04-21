using CrestApps.Core.AI.Profiles;

namespace CrestApps.Core.Blazor.Web.Services;

public sealed class ArticleAIReferenceLinkResolver : IAIReferenceLinkResolver
{
    public string ResolveLink(string referenceId, IDictionary<string, object> metadata)
    {
        if (string.IsNullOrWhiteSpace(referenceId))
        {
            return null;
        }

        return $"/articles/{referenceId}";
    }
}
