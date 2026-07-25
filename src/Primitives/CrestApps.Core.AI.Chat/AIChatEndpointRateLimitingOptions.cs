using Microsoft.AspNetCore.Http;

namespace CrestApps.Core.AI.Chat;

/// <summary>
/// Configures the ASP.NET Core endpoint rate-limit policies used for AI chat hosts.
/// </summary>
public sealed class AIChatEndpointRateLimitingOptions
{
    /// <summary>
    /// Gets or sets the policy name used for anonymous session-start endpoint throttling.
    /// </summary>
    public string AnonymousSessionStartPolicyName { get; set; } = ChatRateLimitPolicyNames.AnonymousSessionStart;

    /// <summary>
    /// Gets or sets a value indicating whether the shared anonymous session-start policy should be registered.
    /// </summary>
    public bool RegisterAnonymousSessionStartPolicy { get; set; } = true;

    /// <summary>
    /// Gets or sets the HTTP status code returned when a shared chat endpoint rate limit rejects a request.
    /// </summary>
    public int RejectionStatusCode { get; set; } = StatusCodes.Status429TooManyRequests;

    /// <summary>
    /// Gets or sets the error message returned when a shared chat endpoint rate limit rejects a request.
    /// </summary>
    public string AnonymousSessionStartErrorMessage { get; set; } = "Too many anonymous chat requests. Please slow down and try again.";
}
