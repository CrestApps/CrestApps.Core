using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace CrestApps.Core.Filters;

/// <summary>
/// A Minimal API endpoint filter that validates the antiforgery token of a request the browser authenticated on its own,
/// which is what cookie authentication does: a page on another site can make the browser send the cookie along with a
/// forged POST. A request that authenticates with an <c>Authorization</c> header (a bearer token or an API key), and an
/// anonymous request, carries no ambient credential to abuse, so neither needs a token.
/// <para>
/// The token is read from the <c>RequestVerificationToken</c> header, or from the form field of the same name, as the
/// host's <see cref="AntiforgeryOptions"/> say. The endpoint keeps <c>DisableAntiforgery()</c> so the antiforgery
/// middleware does not validate the request a second time, and this filter makes the decision instead.
/// </para>
/// </summary>
public sealed class CookieAuthenticationAntiforgeryEndpointFilter : IEndpointFilter
{
    /// <summary>
    /// Validates the antiforgery token when the request is authenticated without an <c>Authorization</c> header, and
    /// otherwise calls the next filter.
    /// </summary>
    /// <param name="context">The invocation context.</param>
    /// <param name="next">The next filter or the endpoint.</param>
    /// <returns>A bad request result when the token is missing or invalid, otherwise the result of the endpoint.</returns>
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var httpContext = context.HttpContext;

        if (RequiresValidation(httpContext.Request, httpContext.User))
        {
            var antiforgery = httpContext.RequestServices.GetRequiredService<IAntiforgery>();

            try
            {
                await antiforgery.ValidateRequestAsync(httpContext);
            }
            catch (AntiforgeryValidationException)
            {
                return TypedResults.BadRequest("The antiforgery token is missing or invalid.");
            }
        }

        return await next(context);
    }

    private static bool RequiresValidation(HttpRequest request, ClaimsPrincipal user)
    {
        if (HttpMethods.IsGet(request.Method) ||
            HttpMethods.IsHead(request.Method) ||
            HttpMethods.IsOptions(request.Method) ||
            HttpMethods.IsTrace(request.Method))
        {
            return false;
        }

        if (user?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        return !request.Headers.ContainsKey(HeaderNames.Authorization);
    }
}
