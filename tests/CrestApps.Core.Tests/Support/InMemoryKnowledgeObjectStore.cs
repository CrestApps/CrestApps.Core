using System.Text.Json.Nodes;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Models;

namespace CrestApps.Core.Tests.Support;

/// <summary>
/// Keeps knowledge objects in memory so ingestion can be exercised without a database.
/// </summary>
internal sealed class InMemoryKnowledgeObjectStore : IKnowledgeObjectStore
{
    private readonly List<KnowledgeObject> _entries = [];

    /// <summary>
    /// Gets everything currently stored.
    /// </summary>
    public IReadOnlyList<KnowledgeObject> All => _entries;

    /// <summary>
    /// Adds an object without going through the create path.
    /// </summary>
    /// <param name="entry">The object.</param>
    public void Seed(KnowledgeObject entry)
    {
        _entries.Add(entry);
    }

    public ValueTask CreateAsync(KnowledgeObject entry, CancellationToken cancellationToken = default)
    {
        _entries.Add(entry);

        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateAsync(KnowledgeObject entry, CancellationToken cancellationToken = default)
    {
        var index = _entries.FindIndex(item => item.ItemId == entry.ItemId);

        if (index >= 0)
        {
            _entries[index] = entry;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> DeleteAsync(KnowledgeObject entry, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_entries.RemoveAll(item => item.ItemId == entry.ItemId) > 0);
    }

    public ValueTask<KnowledgeObject> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_entries.FirstOrDefault(item => item.ItemId == id));
    }

    public ValueTask<IReadOnlyCollection<KnowledgeObject>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<IReadOnlyCollection<KnowledgeObject>>(_entries.ToArray());
    }

    public ValueTask<IReadOnlyCollection<KnowledgeObject>> GetAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        var wanted = new HashSet<string>(ids, StringComparer.Ordinal);

        return ValueTask.FromResult<IReadOnlyCollection<KnowledgeObject>>(
            _entries.Where(item => wanted.Contains(item.ItemId)).ToArray());
    }

    public static ValueTask<KnowledgeObject> NewAsync(string source, JsonNode data = null, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(new KnowledgeObject
        {
            ItemId = Guid.NewGuid().ToString("N"),
            Source = source,
        });
    }

    public ValueTask<IReadOnlyCollection<KnowledgeObject>> GetAsync(string source, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<IReadOnlyCollection<KnowledgeObject>>(
            _entries.Where(item => item.Source == source).ToArray());
    }

    public ValueTask<PageResult<KnowledgeObject>> PageAsync<TQuery>(int page, int pageSize, TQuery context, CancellationToken cancellationToken = default)
        where TQuery : QueryContext
    {
        var items = _entries.Skip((page - 1) * pageSize).Take(pageSize).ToArray();

        return ValueTask.FromResult(new PageResult<KnowledgeObject>
        {
            Count = _entries.Count,
            Entries = items,
        });
    }

    public Task<KnowledgeObject> FindByCanonicalIdAsync(string dataSourceId, string canonicalId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_entries.FirstOrDefault(item => item.Source == dataSourceId && item.CanonicalId == canonicalId));
    }

    public Task<IReadOnlyCollection<KnowledgeObject>> GetByCanonicalIdsAsync(string dataSourceId, IEnumerable<string> canonicalIds, CancellationToken cancellationToken = default)
    {
        var wanted = new HashSet<string>(canonicalIds, StringComparer.Ordinal);

        return Task.FromResult<IReadOnlyCollection<KnowledgeObject>>(
            _entries.Where(item => item.Source == dataSourceId && wanted.Contains(item.CanonicalId)).ToArray());
    }

    public Task<IReadOnlyCollection<KnowledgeObject>> GetByRootIdAsync(string dataSourceId, string rootId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyCollection<KnowledgeObject>>(
            _entries.Where(item => item.Source == dataSourceId && item.RootId == rootId).ToArray());
    }

    public Task<IReadOnlyCollection<KnowledgeObject>> GetByDataSourceIdAsync(string dataSourceId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyCollection<KnowledgeObject>>(
            _entries.Where(item => item.Source == dataSourceId).ToArray());
    }

    public Task<IReadOnlyCollection<KnowledgeObject>> GetByStatusAsync(string dataSourceId, string status, int take, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyCollection<KnowledgeObject>>(
            _entries.Where(item => item.Source == dataSourceId && item.Status == status).Take(take).ToArray());
    }

    public Task<KnowledgeObject> FindFigureByContentHashAsync(string contentHash, string promptVersion, CancellationToken cancellationToken = default)
    {
        foreach (var entry in _entries)
        {
            if (entry.ObjectType is not (KnowledgeContentTypes.Figure or KnowledgeContentTypes.Chart))
            {
                continue;
            }

            if (entry.ContentHash != contentHash || !entry.TryGet<FigureDetails>(out var details))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(details.Description))
            {
                continue;
            }

            if (string.IsNullOrEmpty(promptVersion) || details.DescriptionPromptVersion == promptVersion)
            {
                return Task.FromResult(entry);
            }
        }

        return Task.FromResult<KnowledgeObject>(null);
    }

    public Task DeleteByRootIdAsync(string dataSourceId, string rootId, CancellationToken cancellationToken = default)
    {
        _entries.RemoveAll(item => item.Source == dataSourceId && item.RootId == rootId);

        return Task.CompletedTask;
    }
}
