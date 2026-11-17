using System.Net;
using System.Net.Sockets;
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

        return ResolveNetworkAddressKey(visitorIdentity.RemoteAddressHash, visitorIdentity.RemoteAddress);
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
        return ResolveNetworkAddressKey(context.RemoteAddressHash, context.RemoteAddress);
    }

    private static string ResolveNetworkAddressKey(string remoteAddressHash, string remoteAddress)
    {
        if (!string.IsNullOrWhiteSpace(remoteAddressHash))
        {
            return $"ip-hash:{remoteAddressHash}";
        }

        if (!string.IsNullOrWhiteSpace(remoteAddress))
        {
            return $"ip:{ToNetworkKey(remoteAddress.Trim())}";
        }

        return null;
    }

    // An IPv6 client usually holds a whole /64, so a client that rotates inside it would get a fresh allowance for every
    // address. The hashed and encrypted visitor modes already hash the /64; a plain address (the plain-text mode, or a
    // host that fills RemoteAddress itself) is reduced here, so every mode keys on the same network. The stored address
    // is not changed. Anything that is not an IPv6 address (IPv4, "unknown", an encrypted value) is kept as it is.
    private static string ToNetworkKey(string remoteAddress)
    {
        if (!IPAddress.TryParse(remoteAddress, out var address) ||
            address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return remoteAddress;
        }

        // ::ffff:a.b.c.d and a.b.c.d are one client.
        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4().ToString();
        }

        var bytes = address.GetAddressBytes();

        Array.Clear(bytes, 8, 8);

        return new IPAddress(bytes) + "/64";
    }
}
