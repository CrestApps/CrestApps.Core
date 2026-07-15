using CrestApps.Core.AI.Security;
using Microsoft.AspNetCore.Http;

namespace CrestApps.Core.AI.Chat;

/// <summary>
/// Ensures anonymous browser traffic receives a stable visitor cookie for AI chat analytics and rate limiting.
/// </summary>
public sealed class AIAnonymousVisitorCookieMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIAnonymousVisitorCookieMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware.</param>
    public AIAnonymousVisitorCookieMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Processes the current request.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="visitorIdentityResolver">The visitor identity resolver.</param>
    public Task InvokeAsync(HttpContext context, IAIVisitorIdentityResolver visitorIdentityResolver)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(visitorIdentityResolver);

        if ((HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)) &&
            context.User.Identity?.IsAuthenticated != true)
        {
            _ = visitorIdentityResolver.Resolve();
        }

        return _next(context);
    }
}
