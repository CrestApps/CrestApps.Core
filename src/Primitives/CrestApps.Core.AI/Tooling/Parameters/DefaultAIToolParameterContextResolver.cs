using System.Globalization;
using System.Security.Claims;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Tooling.Parameters;

/// <summary>
/// Resolves the framework's built-in context keys: the current user's identity from the ambient
/// <see cref="ClaimsPrincipal"/>, the identifier of the resource driving the completion, and the current
/// UTC time.
/// </summary>
public sealed class DefaultAIToolParameterContextResolver : IAIToolParameterContextResolver
{
    private static readonly AIToolParameterContextKey[] _supportedKeys =
    [
        new(AIToolParameterContextKeys.UserId, "Current user ID", "The signed-in user's unique identifier."),
        new(AIToolParameterContextKeys.UserName, "Current user name", "The signed-in user's display name."),
        new(AIToolParameterContextKeys.UserEmail, "Current user email", "The signed-in user's email address."),
        new(AIToolParameterContextKeys.ResourceId, "Resource ID", "The identifier of the chat interaction or profile driving the request."),
        new(AIToolParameterContextKeys.UtcNow, "Current UTC time", "The current time in round-trip UTC format."),
    ];

    /// <inheritdoc />
    public IReadOnlyList<AIToolParameterContextKey> SupportedKeys => _supportedKeys;

    /// <inheritdoc />
    public bool TryResolve(string contextKey, IServiceProvider services, out object value)
    {
        value = null;

        if (string.IsNullOrEmpty(contextKey))
        {
            return false;
        }

        switch (contextKey.ToLowerInvariant())
        {
            case AIToolParameterContextKeys.UserId:
                value = GetClaim(services, ClaimTypes.NameIdentifier);

                return value is not null;

            case AIToolParameterContextKeys.UserName:
                value = GetUser(services)?.Identity?.Name ?? GetClaim(services, ClaimTypes.Name);

                return value is not null;

            case AIToolParameterContextKeys.UserEmail:
                value = GetClaim(services, ClaimTypes.Email);

                return value is not null;

            case AIToolParameterContextKeys.ResourceId:
                value = GetResourceId();

                return value is not null;

            case AIToolParameterContextKeys.UtcNow:
                var timeProvider = services?.GetService<TimeProvider>() ?? TimeProvider.System;
                value = timeProvider.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

                return true;

            default:
                return false;
        }
    }

    private static string GetResourceId()
    {
        var resource = AIInvocationScope.Current?.ToolExecutionContext?.Resource;

        return resource is CatalogItem item
            ? item.ItemId
            : null;
    }

    private static ClaimsPrincipal GetUser(IServiceProvider services)
        => services?.GetService<IHttpContextAccessor>()?.HttpContext?.User;

    private static string GetClaim(IServiceProvider services, string claimType)
    {
        var value = GetUser(services)?.FindFirstValue(claimType);

        return string.IsNullOrEmpty(value)
            ? null
            : value;
    }
}
