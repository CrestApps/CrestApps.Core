using System.Text.Json.Nodes;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Models;

namespace CrestApps.Core.Tests.Support;

/// <summary>
/// Keeps per-item source state in memory so a run can be exercised without a database.
/// </summary>
internal sealed class InMemoryWebCrawlStateStore : IWebCrawlStateStore
{
    private readonly List<WebCrawlState> _entries = [];

    /// <summary>
    /// Gets everything currently stored.
    /// </summary>
    public IReadOnlyList<WebCrawlState> All => _entries;

    public ValueTask CreateAsync(WebCrawlState entry, CancellationToken cancellationToken = default)
    {
        _entries.Add(entry);

        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateAsync(WebCrawlState entry, CancellationToken cancellationToken = default)
    {
        var index = _entries.FindIndex(item => item.ItemId == entry.ItemId);

        if (index >= 0)
        {
            _entries[index] = entry;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> DeleteAsync(WebCrawlState entry, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_entries.RemoveAll(item => item.ItemId == entry.ItemId) > 0);
    }

    public ValueTask<WebCrawlState> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_entries.FirstOrDefault(item => item.ItemId == id));
    }

    public ValueTask<IReadOnlyCollection<WebCrawlState>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<IReadOnlyCollection<WebCrawlState>>(_entries.ToArray());
    }

    public ValueTask<IReadOnlyCollection<WebCrawlState>> GetAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        var wanted = new HashSet<string>(ids, StringComparer.Ordinal);

        return ValueTask.FromResult<IReadOnlyCollection<WebCrawlState>>(
            _entries.Where(item => wanted.Contains(item.ItemId)).ToArray());
    }

    public ValueTask<IReadOnlyCollection<WebCrawlState>> GetAsync(string source, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<IReadOnlyCollection<WebCrawlState>>(
            _entries.Where(item => item.Source == source).ToArray());
    }

    public static ValueTask<WebCrawlState> NewAsync(string source, JsonNode data = null, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(new WebCrawlState
        {
            ItemId = Guid.NewGuid().ToString("N"),
            Source = source,
        });
    }

    public ValueTask<PageResult<WebCrawlState>> PageAsync<TQuery>(int page, int pageSize, TQuery context, CancellationToken cancellationToken = default)
        where TQuery : QueryContext
    {
        var items = _entries.Skip((page - 1) * pageSize).Take(pageSize).ToArray();

        return ValueTask.FromResult(new PageResult<WebCrawlState>
        {
            Count = _entries.Count,
            Entries = items,
        });
    }

    public Task DeleteByCrawlerIdAsync(string webCrawlerId, CancellationToken cancellationToken = default)
    {
        _entries.RemoveAll(item => item.Source == webCrawlerId);

        return Task.CompletedTask;
    }

    public Task DeleteByUrlsAsync(string webCrawlerId, IEnumerable<string> urls, CancellationToken cancellationToken = default)
    {
        var wanted = new HashSet<string>(urls, StringComparer.Ordinal);

        _entries.RemoveAll(item => item.Source == webCrawlerId && wanted.Contains(item.Url));

        return Task.CompletedTask;
    }
}
