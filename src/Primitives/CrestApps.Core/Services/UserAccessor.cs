using System.Security.Claims;
using CrestApps.Core.Security;
using Microsoft.AspNetCore.Http;

namespace CrestApps.Core.Services;

/// <summary>
/// Default <see cref="IUserAccessor"/> implementation. The assigned principal is tracked with an
/// <see cref="AsyncLocal{T}"/>, following the same pattern as <see cref="HttpContextAccessor"/>, so it flows
/// across asynchronous continuations without depending on the service scope. When no principal has been
/// assigned, the accessor falls back to the principal of the current HTTP request.
/// </summary>
public sealed class UserAccessor : IUserAccessor
{
    private static readonly AsyncLocal<UserHolder> _current = new();

    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserAccessor"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor used when no principal has been assigned.</param>
    public UserAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public ClaimsPrincipal? User
    {
        get => _current.Value?.User ?? _httpContextAccessor.HttpContext?.User;
        set
        {
            var holder = _current.Value;

            if (holder is not null)
            {
                // Detach the principal from the holder so that asynchronous flows which already
                // captured it stop observing the previous value.
                holder.User = null;
            }

            if (value is not null)
            {
                _current.Value = new UserHolder
                {
                    User = value,
                };
            }
        }
    }

    private sealed class UserHolder
    {
        public ClaimsPrincipal? User { get; set; }
    }
}
