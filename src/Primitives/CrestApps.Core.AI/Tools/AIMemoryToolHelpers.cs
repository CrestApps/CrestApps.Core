using System.Security.Claims;
using CrestApps.Core.AI.Memory;
using CrestApps.Core.AI.Orchestration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Tools;

internal static class AIMemoryToolHelpers
{
    /// <summary>
    /// Gets current user id.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static string GetCurrentUserId(IServiceProvider services = null)
    {
        var invocationContext = AIInvocationScope.Current;

        if (invocationContext?.Items.TryGetValue(MemoryConstants.CompletionContextKeys.UserId, out var userId) == true)
        {
            return userId as string;
        }

        return services?
            .GetService<IHttpContextAccessor>()?
            .HttpContext?
            .User?
            .FindFirstValue(ClaimTypes.NameIdentifier)
            ?? services?
                .GetService<IHttpContextAccessor>()?
                .HttpContext?
                .User?
                .FindFirstValue(ClaimTypes.Name);
    }
}
