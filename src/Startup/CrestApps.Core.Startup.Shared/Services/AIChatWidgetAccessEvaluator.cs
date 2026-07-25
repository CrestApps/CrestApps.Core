using System.Security.Claims;

namespace CrestApps.Core.Startup.Shared.Services;

/// <summary>
/// Evaluates whether a caller can access the sample AI chat widget profile.
/// </summary>
public static class AIChatWidgetAccessEvaluator
{
    /// <summary>
    /// Determines whether the current caller can access the requested chat profile.
    /// </summary>
    /// <param name="user">The current caller.</param>
    /// <param name="widgetProfileId">The profile configured for the sample widget.</param>
    /// <param name="requestedProfileId">The requested chat profile identifier.</param>
    /// <returns><see langword="true"/> when access should be allowed; otherwise <see langword="false"/>.</returns>
    public static bool CanAccessProfile(
        ClaimsPrincipal user,
        string widgetProfileId,
        string requestedProfileId)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.Identity?.IsAuthenticated == true)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(widgetProfileId) &&
            string.Equals(widgetProfileId, requestedProfileId, StringComparison.Ordinal);
    }
}
