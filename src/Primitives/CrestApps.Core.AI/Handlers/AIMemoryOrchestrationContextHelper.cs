using System.Security.Claims;
using CrestApps.Core.AI.Models;
using Microsoft.AspNetCore.Http;

namespace CrestApps.Core.AI.Handlers;

internal static class AIMemoryOrchestrationContextHelper
{
    /// <summary>
    /// Gets authenticated user id.
    /// </summary>
    /// <param name="httpContextAccessor">The http context accessor.</param>
    public static string GetAuthenticatedUserId(IHttpContextAccessor httpContextAccessor)
    {
        return httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true ? httpContextAccessor.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? httpContextAccessor.HttpContext.User.FindFirstValue(ClaimTypes.Name) : null;
    }

    /// <summary>
    /// Determines whether enabled.
    /// </summary>
    /// <param name="resource">The resource.</param>
    /// <param name="chatInteractionMemoryOptions">The chat interaction memory options.</param>
    public static bool IsEnabled(object resource, ChatInteractionMemoryOptions chatInteractionMemoryOptions)
    {
        if (resource is AIProfile profile)
        {
            return profile.TryGet<MemoryMetadata>(out var metadata) && metadata.EnableUserMemory == true;
        }

        if (resource is ChatInteraction)
        {
            return chatInteractionMemoryOptions.EnableUserMemory;
        }

        return false;
    }
}
