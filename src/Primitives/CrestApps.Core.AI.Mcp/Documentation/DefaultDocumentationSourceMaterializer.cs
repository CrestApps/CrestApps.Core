using System.Collections.Concurrent;

namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// The default <see cref="IDocumentationSourceMaterializer"/>. It keeps one cached
/// <see cref="IDocumentationSource"/> per key and rebuilds it only when the supplied signature changes,
/// so a crawled corpus or a downloaded search index survives across searches for the lifetime of the
/// application.
/// </summary>
public sealed class DefaultDocumentationSourceMaterializer : IDocumentationSourceMaterializer
{
    private readonly ConcurrentDictionary<string, CachedSource> _cache = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public IDocumentationSource GetOrCreate(string key, string signature, Func<IDocumentationSource> factory)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(factory);

        var cached = _cache.AddOrUpdate(
            key,
            _ => new CachedSource(signature, factory()),
            (_, existing) => string.Equals(existing.Signature, signature, StringComparison.Ordinal)
                ? existing
                : new CachedSource(signature, factory()));

        return cached.Source;
    }

    private sealed class CachedSource
    {
        public CachedSource(string signature, IDocumentationSource source)
        {
            Signature = signature;
            Source = source;
        }

        public string Signature { get; }

        public IDocumentationSource Source { get; }
    }
}
