using System.Threading.RateLimiting;
using CrestApps.Core.AI.Chat;
using CrestApps.Core.AI.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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

/// <summary>
/// Configures the shared ASP.NET Core rate-limiter policies used by the sample hosts.
/// </summary>
public sealed class ConfigureAIChatEndpointRateLimiterOptions : IConfigureOptions<RateLimiterOptions>
{
    private readonly IOptions<AIChatEndpointRateLimitingOptions> _endpointRateLimitingOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigureAIChatEndpointRateLimiterOptions"/> class.
    /// </summary>
    /// <param name="endpointRateLimitingOptions">The shared AI chat endpoint rate-limiting options.</param>
    public ConfigureAIChatEndpointRateLimiterOptions(IOptions<AIChatEndpointRateLimitingOptions> endpointRateLimitingOptions)
    {
        _endpointRateLimitingOptions = endpointRateLimitingOptions;
    }

    /// <summary>
    /// Configures the ASP.NET Core rate-limiter options for AI chat sample hosts.
    /// </summary>
    /// <param name="options">The rate-limiter options to configure.</param>
    public void Configure(RateLimiterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var endpointOptions = _endpointRateLimitingOptions.Value;

        options.OnRejected = static (context, cancellationToken) =>
        {
            if (context.HttpContext.Response.HasStarted)
            {
                return ValueTask.CompletedTask;
            }

            var endpointOptions = context.HttpContext.RequestServices.GetRequiredService<IOptions<AIChatEndpointRateLimitingOptions>>().Value;
            context.HttpContext.Response.StatusCode = endpointOptions.RejectionStatusCode;

            return new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(
                new
                {
                    error = endpointOptions.AnonymousSessionStartErrorMessage,
                },
                cancellationToken: cancellationToken));
        };

        options.GlobalLimiter = null;
        options.RejectionStatusCode = endpointOptions.RejectionStatusCode;

        options.AddPolicy<string>(endpointOptions.AnonymousSessionStartPolicyName, static httpContext =>
        {
            var endpointOptions = httpContext.RequestServices.GetRequiredService<IOptions<AIChatEndpointRateLimitingOptions>>().Value;

            if (!endpointOptions.RegisterAnonymousSessionStartPolicy)
            {
                return RateLimitPartition.GetNoLimiter("disabled");
            }

            var promptSecurityOptions = httpContext.RequestServices.GetRequiredService<IOptionsMonitor<PromptSecurityOptions>>().CurrentValue;

            if (promptSecurityOptions.MaxAnonymousSessionsPerWindow <= 0 ||
                httpContext.User.Identity?.IsAuthenticated == true)
            {
                return RateLimitPartition.GetNoLimiter("disabled");
            }

            var rateLimitingOptions = httpContext.RequestServices.GetRequiredService<IOptions<AIChatRateLimitingOptions>>().Value;
            var visitorIdentityResolver = httpContext.RequestServices.GetRequiredService<IAIVisitorIdentityResolver>();
            var visitorIdentity = visitorIdentityResolver.Resolve();
            var context = new PromptSecurityContext
            {
                User = httpContext.User,
                VisitorId = visitorIdentity.VisitorId,
                RemoteAddress = visitorIdentity.RemoteAddress,
                RemoteAddressHash = visitorIdentity.RemoteAddressHash,
            };
            var keys = ChatRateLimitKeyResolver.ResolveAnonymousSessionStartKeys(context, rateLimitingOptions);
            var partitionKey = keys.Count > 0 ? string.Join("|", keys.OrderBy(static key => key, StringComparer.Ordinal)) : "anonymous";

            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = promptSecurityOptions.MaxAnonymousSessionsPerWindow,
                    Window = promptSecurityOptions.AnonymousSessionRateLimitWindow,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
        });
    }
}
