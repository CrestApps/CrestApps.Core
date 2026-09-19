using CrestApps.Core.AI.Chat;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Startup.Shared.Services;

/// <summary>
/// Configures shared visitor-tracking and endpoint protection for AI chat sample hosts.
/// </summary>
public static class AIChatProtectionExtensions
{
    /// <summary>
    /// Adds the shared AI chat rate-limit policies used by the sample hosts.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddSharedAIChatProtection(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<AIChatEndpointRateLimitingOptions>();
        services.AddRateLimiter();
        services.AddSingleton<IConfigureOptions<RateLimiterOptions>, ConfigureAIChatEndpointRateLimiterOptions>();

        return services;
    }

    /// <summary>
    /// Adds the shared AI chat middleware used by the sample hosts.
    /// </summary>
    /// <param name="app">The application builder.</param>
    public static IApplicationBuilder UseSharedAIChatProtection(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseAIAnonymousVisitorCookie()
            .UseRateLimiter();
    }
}
