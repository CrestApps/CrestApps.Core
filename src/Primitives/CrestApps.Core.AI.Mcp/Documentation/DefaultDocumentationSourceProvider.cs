using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// Default <see cref="IDocumentationSourceProvider"/> that combines documentation sources defined in
/// options (through the documentation search builder), documentation sources persisted in a catalog
/// (for example a YesSql or EntityCore store), and custom <see cref="IDocumentationSource"/>
/// implementations registered in code. Materialized crawler sources are cached and only rebuilt when
/// their defining entry changes so their in-memory corpus is reused across searches.
/// </summary>
public sealed class DefaultDocumentationSourceProvider : IDocumentationSourceProvider
{
    private const string OptionsSignature = "options";

    private readonly IOptions<DocumentationSearchOptions> _options;
    private readonly Dictionary<string, IDocumentationSourceFactory> _factories;
    private readonly ConcurrentDictionary<string, CachedSource> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultDocumentationSourceProvider"/> class.
    /// </summary>
    /// <param name="options">The documentation search options.</param>
    /// <param name="factories">The strategy factories used to materialize stored and options-defined entries.</param>
    public DefaultDocumentationSourceProvider(
        IOptions<DocumentationSearchOptions> options,
        IEnumerable<IDocumentationSourceFactory> factories)
    {
        _options = options;

        var map = new Dictionary<string, IDocumentationSourceFactory>(StringComparer.OrdinalIgnoreCase);

        foreach (var factory in factories)
        {
            map[factory.Strategy] = factory;
        }

        _factories = map;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<IDocumentationSource>> GetSourcesAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var sources = new List<IDocumentationSource>();

        foreach (var source in services.GetServices<IDocumentationSource>())
        {
            sources.Add(source);
        }

        var descriptors = new List<MaterializedDescriptor>();

        CollectOptionsDescriptors(descriptors);

        await CollectCatalogDescriptorsAsync(services, descriptors, cancellationToken);

        var liveKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var descriptor in descriptors)
        {
            liveKeys.Add(descriptor.Key);

            var cached = _cache.AddOrUpdate(
                descriptor.Key,
                _ => new CachedSource(descriptor.Signature, descriptor.Factory.Create(descriptor.Entry)),
                (_, existing) => string.Equals(existing.Signature, descriptor.Signature, StringComparison.Ordinal)
                    ? existing
                    : new CachedSource(descriptor.Signature, descriptor.Factory.Create(descriptor.Entry)));

            sources.Add(cached.Source);
        }

        foreach (var key in _cache.Keys)
        {
            if (!liveKeys.Contains(key))
            {
                _cache.TryRemove(key, out _);
            }
        }

        return sources;
    }

    private void CollectOptionsDescriptors(List<MaterializedDescriptor> descriptors)
    {
        var options = _options.Value;

        if (_factories.TryGetValue(DocumentationSourceStrategies.Sitemap, out var sitemapFactory))
        {
            foreach (var site in options.Sites)
            {
                if (string.IsNullOrWhiteSpace(site.Name) || string.IsNullOrWhiteSpace(site.BaseUrl))
                {
                    continue;
                }

                var entry = new DocumentationSourceEntry
                {
                    Name = site.Name,
                    Source = DocumentationSourceStrategies.Sitemap,
                    BaseUrl = site.BaseUrl,
                    SitemapUrl = site.SitemapUrl,
                    MaxResults = site.MaxResults,
                    MaxPages = site.MaxPages,
                };

                descriptors.Add(new MaterializedDescriptor($"options:sitemap:{site.Name}", OptionsSignature, sitemapFactory, entry));
            }
        }

        if (_factories.TryGetValue(DocumentationSourceStrategies.SearchIndex, out var searchIndexFactory))
        {
            foreach (var site in options.SearchIndexes)
            {
                if (string.IsNullOrWhiteSpace(site.Name) || string.IsNullOrWhiteSpace(site.BaseUrl))
                {
                    continue;
                }

                var entry = new DocumentationSourceEntry
                {
                    Name = site.Name,
                    Source = DocumentationSourceStrategies.SearchIndex,
                    BaseUrl = site.BaseUrl,
                    IndexUrl = site.IndexUrl,
                    MaxResults = site.MaxResults,
                };

                descriptors.Add(new MaterializedDescriptor($"options:search-index:{site.Name}", OptionsSignature, searchIndexFactory, entry));
            }
        }

        if (_factories.TryGetValue(DocumentationSourceStrategies.Algolia, out var algoliaFactory))
        {
            foreach (var site in options.AlgoliaSources)
            {
                if (string.IsNullOrWhiteSpace(site.Name)
                    || string.IsNullOrWhiteSpace(site.ApplicationId)
                    || string.IsNullOrWhiteSpace(site.ApiKey)
                    || string.IsNullOrWhiteSpace(site.IndexName))
                {
                    continue;
                }

                var entry = new DocumentationSourceEntry
                {
                    Name = site.Name,
                    Source = DocumentationSourceStrategies.Algolia,
                    ApplicationId = site.ApplicationId,
                    ApiKey = site.ApiKey,
                    IndexName = site.IndexName,
                    MaxResults = site.MaxResults,
                };

                descriptors.Add(new MaterializedDescriptor($"options:algolia:{site.Name}", OptionsSignature, algoliaFactory, entry));
            }
        }
    }

    private async Task CollectCatalogDescriptorsAsync(IServiceProvider services, List<MaterializedDescriptor> descriptors, CancellationToken cancellationToken)
    {
        var catalog = services.GetService<IDocumentationSourceCatalog>();

        if (catalog is null)
        {
            return;
        }

        var entries = await catalog.GetAllAsync(cancellationToken);

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Source) || !_factories.TryGetValue(entry.Source, out var factory))
            {
                continue;
            }

            var signature = (entry.ModifiedUtc ?? entry.CreatedUtc).Ticks.ToString(CultureInfo.InvariantCulture);

            descriptors.Add(new MaterializedDescriptor($"catalog:{entry.ItemId}", signature, factory, entry));
        }
    }

    private sealed class MaterializedDescriptor
    {
        public MaterializedDescriptor(
            string key,
            string signature,
            IDocumentationSourceFactory factory,
            DocumentationSourceEntry entry)
        {
            Key = key;
            Signature = signature;
            Factory = factory;
            Entry = entry;
        }

        public string Key { get; }

        public string Signature { get; }

        public IDocumentationSourceFactory Factory { get; }

        public DocumentationSourceEntry Entry { get; }
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
