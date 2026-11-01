using System.Security.Claims;

namespace CrestApps.Core.AI.WebMcp.Services;

/// <summary>
/// Lets every signed-in user use the WebMCP tool endpoints.
/// </summary>
public sealed class DefaultWebMcpAccessEvaluator : IWebMcpAccessEvaluator
{
    /// <inheritdoc/>
    public Task<bool> CanUseAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(user?.Identity?.IsAuthenticated == true);
    }
}
