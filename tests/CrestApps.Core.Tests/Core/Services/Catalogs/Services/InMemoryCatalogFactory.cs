using CrestApps.Core.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.Tests.Core.Services.Catalogs.Services;

internal static class InMemoryCatalogFactory
{
    internal static ICatalog<TCatalog> CreateCatalog<TCatalog>(IEnumerable<TCatalog> records)
        where TCatalog : CatalogItem
        => new InMemoryCatalog<TCatalog>(records);

    internal static INamedCatalog<TCatalog> CreateNamedCatalog<TCatalog>(IEnumerable<TCatalog> records)
        where TCatalog : CatalogItem, INameAwareModel
        => new InMemoryNamedCatalog<TCatalog>(records);

    internal static INamedSourceCatalog<TCatalog> CreateNamedSourceCatalog<TCatalog>(IEnumerable<TCatalog> records)
        where TCatalog : CatalogItem, INameAwareModel, ISourceAwareModel
        => new InMemoryNamedSourceCatalog<TCatalog>(records);

    private class InMemoryCatalog<TCatalog>(IEnumerable<TCatalog> records) : ICatalog<TCatalog>
        where TCatalog : CatalogItem
    {
        protected readonly List<TCatalog> Records = records?.ToList() ?? [];

        public ValueTask CreateAsync(TCatalog entry, CancellationToken cancellationToken = default)
        {
            Records.Add(entry);

            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> DeleteAsync(TCatalog entry, CancellationToken cancellationToken = default)
        {
            var existing = Records.FirstOrDefault(model => model.ItemId == entry.ItemId);

            if (existing is null)
            {
                return ValueTask.FromResult(false);
            }

            Records.Remove(existing);

            return ValueTask.FromResult(true);
        }

        public ValueTask<TCatalog> FindByIdAsync(string id, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Records.FirstOrDefault(model => model.ItemId == id))!;

        public ValueTask<IReadOnlyCollection<TCatalog>> GetAllAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyCollection<TCatalog>>([.. Records]);

        public ValueTask<IReadOnlyCollection<TCatalog>> GetAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
        {
            var idSet = ids.ToHashSet(StringComparer.Ordinal);

            return ValueTask.FromResult<IReadOnlyCollection<TCatalog>>(
            [
                .. Records.Where(model => model.ItemId is not null && idSet.Contains(model.ItemId)),
            ]);
        }

        public ValueTask<PageResult<TCatalog>> PageAsync<TQuery>(
            int page,
            int pageSize,
            TQuery context,
            CancellationToken cancellationToken = default)
            where TQuery : QueryContext
        {
            var entries = Records
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return ValueTask.FromResult(new PageResult<TCatalog>
            {
                Count = Records.Count,
                Entries = entries,
            });
        }

        public ValueTask UpdateAsync(TCatalog entry, CancellationToken cancellationToken = default)
        {
            var index = Records.FindIndex(model => model.ItemId == entry.ItemId);

            if (index >= 0)
            {
                Records[index] = entry;
            }
            else
            {
                Records.Add(entry);
            }

            return ValueTask.CompletedTask;
        }
    }

    private class InMemoryNamedCatalog<TCatalog>(IEnumerable<TCatalog> records) : InMemoryCatalog<TCatalog>(records), INamedCatalog<TCatalog>
        where TCatalog : CatalogItem, INameAwareModel
    {
        public ValueTask<TCatalog> FindByNameAsync(string name, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Records.FirstOrDefault(model => model.Name == name))!;
    }

    private sealed class InMemoryNamedSourceCatalog<TCatalog>(IEnumerable<TCatalog> records) : InMemoryNamedCatalog<TCatalog>(records), INamedSourceCatalog<TCatalog>
        where TCatalog : CatalogItem, INameAwareModel, ISourceAwareModel
    {
        public ValueTask<IReadOnlyCollection<TCatalog>> GetAsync(string source, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyCollection<TCatalog>>([.. Records.Where(model => model.Source == source)]);

        public ValueTask<TCatalog> GetAsync(string name, string source, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Records.FirstOrDefault(model => model.Name == name && model.Source == source))!;
    }
}
