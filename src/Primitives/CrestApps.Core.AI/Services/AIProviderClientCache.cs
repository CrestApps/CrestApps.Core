using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Holds the SDK clients the AI providers build, so each client is built once and reused for as long as the service
/// container that resolved the cache lives.
/// </summary>
/// <remarks>
/// The cache is registered as a singleton, so each container, and therefore each tenant of a multi-tenant host, keeps
/// its own clients. A client, with its credentials and its transport, is never used by a container that did not build
/// it.
/// </remarks>
public sealed class AIProviderClientCache : IDisposable
{
    private readonly ConcurrentDictionary<(Type ClientType, string Key), object> _clients = new();

    /// <summary>
    /// Gets the client of the given type stored under the key, and builds it the first time the key is asked for.
    /// </summary>
    /// <typeparam name="TClient">The type of the client.</typeparam>
    /// <param name="key">
    /// The key that identifies everything the client is built from, such as its endpoint, credentials and options. A
    /// secret goes into the key through <see cref="HashSecret(string)"/>.
    /// </param>
    /// <param name="createClient">Builds the client when none is stored under the key.</param>
    /// <returns>The client.</returns>
    public TClient GetOrAdd<TClient>(string key, Func<TClient> createClient)
        where TClient : class
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(createClient);

        return (TClient)_clients.GetOrAdd((typeof(TClient), key), static (_, create) => create(), createClient);
    }

    /// <summary>
    /// Removes every client, and disposes those that are disposable, so the next request builds new ones. Call it after
    /// rotating credentials to release the clients that were built with the old ones.
    /// </summary>
    public void Clear()
    {
        foreach (var key in _clients.Keys)
        {
            if (_clients.TryRemove(key, out var client) && client is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    /// <summary>
    /// Removes and disposes the clients the cache holds.
    /// </summary>
    public void Dispose()
    {
        Clear();
    }

    /// <summary>
    /// Hashes a secret, such as an API key, so a cache key can tell two secrets apart without holding either one.
    /// </summary>
    /// <param name="secret">The secret, or <see langword="null"/>.</param>
    /// <returns>The lowercase hexadecimal SHA-256 hash of the secret.</returns>
    public static string HashSecret(string secret)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret ?? string.Empty)));
    }
}
