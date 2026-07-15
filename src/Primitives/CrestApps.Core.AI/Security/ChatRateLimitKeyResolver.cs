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

        if (partitions.HasFlag(ChatRateLimitPartition.AuthenticatedUser))
        {
            var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? context.User?.Identity?.Name;

            if (!string.IsNullOrWhiteSpace(userId))
            {
                keys.Add($"user:{userId}");
            }
        }

        if (keys.Count > 0)
        {
            return keys;
        }

        if (context.User?.Identity?.IsAuthenticated != true)
        {
            return keys;
        }

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
