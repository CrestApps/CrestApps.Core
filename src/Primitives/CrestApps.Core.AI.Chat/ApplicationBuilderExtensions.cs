using Microsoft.AspNetCore.Builder;

namespace CrestApps.Core.AI.Chat;

/// <summary>
/// Extension methods for AI chat middleware.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds middleware that establishes stable anonymous visitor cookies for AI chat traffic.
    /// </summary>
    /// <param name="app">The application builder.</param>
    public static IApplicationBuilder UseAIAnonymousVisitorCookie(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<AIAnonymousVisitorCookieMiddleware>();
    }
}
