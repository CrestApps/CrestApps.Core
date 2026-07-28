using System.Security.Claims;

namespace CrestApps.Core.Security;

/// <summary>
/// Provides access to the <see cref="ClaimsPrincipal"/> that owns the current operation.
/// </summary>
/// <remarks>
/// Services that make security decisions must resolve the caller through this abstraction rather than through
/// <c>IHttpContextAccessor</c>. SignalR dispatches hub methods outside the request pipeline, so
/// <c>IHttpContextAccessor.HttpContext</c> is unreliable during a hub invocation and is frequently <see langword="null"/>.
/// The default implementation returns the principal a hub assigned for the current invocation and falls back to the
/// HTTP request principal when the operation did not originate from a hub.
/// </remarks>
public interface IUserAccessor
{
    /// <summary>
    /// Gets or sets the principal that owns the current operation, or <see langword="null"/> when the operation did
    /// not originate from a caller. A <see langword="null"/> principal indicates a trusted server-side invocation,
    /// such as a background task, rather than an unauthenticated caller. An unauthenticated caller is represented
    /// by a <see cref="ClaimsPrincipal"/> whose identity is not authenticated.
    /// </summary>
    ClaimsPrincipal User { get; set; }
}
