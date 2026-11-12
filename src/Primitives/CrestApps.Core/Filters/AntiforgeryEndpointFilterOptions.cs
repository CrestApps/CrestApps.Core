namespace CrestApps.Core.Filters;

/// <summary>
/// Options for <see cref="CookieAuthenticationAntiforgeryEndpointFilter"/>.
/// </summary>
public sealed class AntiforgeryEndpointFilterOptions
{
    /// <summary>
    /// Gets the authentication types of identities that carry no ambient credential: a token the client sends on purpose,
    /// such as a bearer token, which a page on another site cannot make a browser attach. A request authenticated by one
    /// of them needs no antiforgery token. Every other authenticated request does, including one that also carries an
    /// <c>Authorization</c> header, because Negotiate, NTLM and Basic make the browser attach that header by itself.
    /// </summary>
    /// <remarks>
    /// The names are the <see cref="System.Security.Principal.IIdentity.AuthenticationType"/> of the identity. Add the one
    /// your API authentication scheme produces.
    /// </remarks>
    public ISet<string> NonCookieAuthenticationTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Bearer",
        "Api",
        "ApiKey",
        "OpenIddict.Validation.AspNetCore",
    };

    /// <summary>
    /// Gets the names of cookies that identify an anonymous caller, such as the visitor cookie that owns a visitor's chat
    /// session. A request without an authenticated user still needs a token when it carries one of them, because the
    /// browser sends that cookie by itself and it is what grants access to the visitor's data.
    /// </summary>
    public ISet<string> AmbientCookieNames { get; } = new HashSet<string>(StringComparer.Ordinal);
}
