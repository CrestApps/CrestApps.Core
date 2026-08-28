using System.Security.Claims;

namespace CrestApps.Core.AI.Security;

/// <summary>
/// Builds consistent rate-limit partition keys for AI chat requests.
/// </summary>
public static class ChatRateLimitKeyResolver
{
    /// <summary>
    /// Resolves the message-throttling keys for the provided context.
    /// </summary>
    /// <param name="context">The prompt security context.</param>
    /// <param name="options">The chat rate-limiting options.</param>
    /// <returns>The rate-limit keys to evaluate.</returns>
    public static List<string> ResolveMessageKeys(PromptSecurityContext context, AIChatRateLimitingOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        var authenticatedKeys = ResolveAuthenticatedKeys(context, options.AuthenticatedMessagePartitions);

        if (authenticatedKeys.Count > 0)
        {
            return authenticatedKeys;
        }

        var anonymousKeys = ResolveAnonymousKeys(context, options.AnonymousMessagePartitions);

        return anonymousKeys.Count > 0 ? anonymousKeys : ["unknown"];
    }

    /// <summary>
    /// Resolves the per-user identity key (<c>user:{id}</c>) for an authenticated caller, or
    /// <see langword="null"/> when no user identifier is available.
    /// </summary>
    /// <param name="context">The prompt security context.</param>
    /// <returns>The user identity key, or <see langword="null"/>.</returns>
    public static string ResolveAuthenticatedUserKey(PromptSecurityContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? context.User?.Identity?.Name;

        return string.IsNullOrWhiteSpace(userId) ? null : $"user:{userId}";
    }

    /// <summary>
    /// Resolves the network/visitor/session/connection partition keys for the provided context and
    /// partition flags. Unlike <see cref="ResolveMessageKeys"/>, this never includes the per-user
    /// identity key, so it can be combined with <see cref="ResolveAuthenticatedUserKey"/> to key an
    /// authenticated caller on both their identity and their network address.
    /// </summary>
    /// <param name="context">The prompt security context.</param>
    /// <param name="partitions">The partition flags to include.</param>
    /// <returns>The resolved context keys.</returns>
    public static List<string> ResolveContextKeys(PromptSecurityContext context, ChatRateLimitPartition partitions)
    {
        ArgumentNullException.ThrowIfNull(context);

        var keys = new List<string>();

        AppendAnonymousStyleKeys(keys, context, partitions);

        return keys;
    }

    /// <summary>
    /// Resolves the anonymous session-start throttling keys for the provided context.
    /// </summary>
    /// <param name="context">The prompt security context.</param>
    /// <param name="options">The chat rate-limiting options.</param>
    /// <returns>The rate-limit keys to evaluate.</returns>
    public static List<string> ResolveAnonymousSessionStartKeys(PromptSecurityContext context, AIChatRateLimitingOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        return ResolveAnonymousKeys(context, options.AnonymousSessionStartPartitions);
    }

    /// <summary>
    /// Resolves the network-address partition key for the provided visitor identity.
    /// </summary>
    /// <param name="visitorIdentity">The visitor identity.</param>
    /// <returns>The network-address partition key, if available.</returns>
    public static string ResolveNetworkAddressKey(AIVisitorIdentity visitorIdentity)
    {
        ArgumentNullException.ThrowIfNull(visitorIdentity);

        if (!string.IsNullOrWhiteSpace(visitorIdentity.RemoteAddressHash))
        {
            return $"ip-hash:{visitorIdentity.RemoteAddressHash}";
        }

        if (!string.IsNullOrWhiteSpace(visitorIdentity.RemoteAddress))
        {
            return $"ip:{visitorIdentity.RemoteAddress}";
        }

        return null;
    }

    private static List<string> ResolveAuthenticatedKeys(PromptSecurityContext context, ChatRateLimitPartition partitions)
    {
        var keys = new List<string>();

        // Only authenticated callers are keyed through the authenticated partition set; an anonymous
        // caller falls through to the anonymous keys in ResolveMessageKeys.
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            return keys;
        }

        if (partitions.HasFlag(ChatRateLimitPartition.AuthenticatedUser))
        {
            var userKey = ResolveAuthenticatedUserKey(context);

            if (userKey is not null)
            {
                keys.Add(userKey);
            }
        }

        // Include the network/visitor/session/connection keys requested for authenticated callers
        // (for example the IP hash) in addition to the per-user key, so a caller cannot shed their
        // per-IP allowance by logging out.
        AppendAnonymousStyleKeys(keys, context, partitions);

        return keys;
    }

    private static List<string> ResolveAnonymousKeys(PromptSecurityContext context, ChatRateLimitPartition partitions)
    {
        var keys = new List<string>();

        AppendAnonymousStyleKeys(keys, context, partitions);

        return keys;
    }

    private static void AppendAnonymousStyleKeys(List<string> keys, PromptSecurityContext context, ChatRateLimitPartition partitions)
    {
        if (partitions.HasFlag(ChatRateLimitPartition.Visitor) &&
            !string.IsNullOrWhiteSpace(context.VisitorId))
        {
            keys.Add($"visitor:{context.VisitorId}");
        }

        if (partitions.HasFlag(ChatRateLimitPartition.NetworkAddress))
        {
            var networkAddressKey = ResolveNetworkAddressKey(context);

            if (!string.IsNullOrWhiteSpace(networkAddressKey))
            {
                keys.Add(networkAddressKey);
            }
        }

        if (partitions.HasFlag(ChatRateLimitPartition.Session) &&
            !string.IsNullOrWhiteSpace(context.SessionId))
        {
            keys.Add($"session:{context.SessionId}");
        }

        if (partitions.HasFlag(ChatRateLimitPartition.Connection) &&
            !string.IsNullOrWhiteSpace(context.ConnectionId))
        {
            keys.Add($"conn:{context.ConnectionId}");
        }
    }

    private static string ResolveNetworkAddressKey(PromptSecurityContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.RemoteAddressHash))
        {
            return $"ip-hash:{context.RemoteAddressHash}";
        }

        if (!string.IsNullOrWhiteSpace(context.RemoteAddress))
        {
            return $"ip:{context.RemoteAddress}";
        }

        return null;
    }
}
