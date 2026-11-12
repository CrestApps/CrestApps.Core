using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Filters;

/// <summary>
/// A Minimal API endpoint filter that validates the antiforgery token of a request whose credential the browser attaches
/// on its own: a cookie, but also the <c>Authorization</c> header of Negotiate, NTLM and Basic. A page on another site can
/// make the browser send those along with a forged POST.
/// <para>
/// A request needs no token only when every authenticated identity came from a scheme whose credential a page cannot make
/// the browser attach (<see cref="AntiforgeryEndpointFilterOptions.NonCookieAuthenticationTypes"/>), or when it has no
/// user and none of the cookies in <see cref="AntiforgeryEndpointFilterOptions.AmbientCookieNames"/>. Whether an
/// <c>Authorization</c> header is present decides nothing, because a cookie-authenticated request can carry any value.
/// </para>
/// <para>
/// The token is read from the <c>RequestVerificationToken</c> header, or from the form field (<c>__RequestVerificationToken</c>
/// by default), as the host's <see cref="AntiforgeryOptions"/> say. The endpoint keeps <c>DisableAntiforgery()</c> so the
/// antiforgery middleware does not validate the request a second time, and this filter makes the decision instead. The host
/// must register antiforgery services (<c>AddAntiforgery()</c>).
/// </para>
/// </summary>
public sealed class CookieAuthenticationAntiforgeryEndpointFilter : IEndpointFilter
{
    private readonly AntiforgeryEndpointFilterOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="CookieAuthenticationAntiforgeryEndpointFilter"/> class with the default
    /// options.
    /// </summary>
    public CookieAuthenticationAntiforgeryEndpointFilter()
        : this(Options.Create(new AntiforgeryEndpointFilterOptions()))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CookieAuthenticationAntiforgeryEndpointFilter"/> class.
    /// </summary>
    /// <param name="options">The filter options.</param>
    [ActivatorUtilitiesConstructor]
    public CookieAuthenticationAntiforgeryEndpointFilter(IOptions<AntiforgeryEndpointFilterOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Validates the antiforgery token when the request carries a credential the browser attaches by itself, and otherwise
    /// calls the next filter.
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

    private bool RequiresValidation(HttpRequest request, ClaimsPrincipal user)
    {
        if (HttpMethods.IsGet(request.Method) ||
            HttpMethods.IsHead(request.Method) ||
            HttpMethods.IsOptions(request.Method) ||
            HttpMethods.IsTrace(request.Method))
        {
            return false;
        }

        var authenticated = user?.Identities.Where(identity => identity.IsAuthenticated).ToList() ?? [];

        if (authenticated.Count > 0)
        {
            return !authenticated.All(identity => _options.NonCookieAuthenticationTypes.Contains(identity.AuthenticationType ?? string.Empty));
        }

        foreach (var cookieName in _options.AmbientCookieNames)
        {
            if (request.Cookies.ContainsKey(cookieName))
            {
                return true;
            }
        }

        return false;
    }
}
