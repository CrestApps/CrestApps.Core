using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Data.YesSql.Indexes.Knowledge;
using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.Extensions.Options;
using YesSql;
using YesSql.Services;

namespace CrestApps.Core.Data.YesSql.Services;

/// <summary>
/// YesSql-backed <see cref="IKnowledgeObjectStore"/>.
/// </summary>
public sealed class YesSqlKnowledgeObjectStore : DocumentCatalog<KnowledgeObject, KnowledgeObjectIndex>, IKnowledgeObjectStore
{
    private static readonly string[] _figureTypes = [KnowledgeObjectTypes.Figure, KnowledgeObjectTypes.Chart];

    /// <summary>
    /// Initializes a new instance of the <see cref="YesSqlKnowledgeObjectStore"/> class.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="options">The options.</param>
    public YesSqlKnowledgeObjectStore(
        ISession session,
        IOptions<YesSqlStoreOptions> options)
        : base(session, options.Value.AICollectionName)
    {
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyCollection<KnowledgeObject>> GetAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);

        return (await Session.Query<KnowledgeObject, KnowledgeObjectIndex>(x => x.Source == source, collection: CollectionName).ListAsync(cancellationToken)).ToArray();
    }

    /// <inheritdoc />
    public async Task<KnowledgeObject> FindByCanonicalIdAsync(string dataSourceId, string canonicalId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataSourceId);
        ArgumentException.ThrowIfNullOrEmpty(canonicalId);

        return await Session
            .Query<KnowledgeObject, KnowledgeObjectIndex>(x => x.Source == dataSourceId && x.CanonicalId == canonicalId, collection: CollectionName)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<KnowledgeObject>> GetByCanonicalIdsAsync(string dataSourceId, IEnumerable<string> canonicalIds, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataSourceId);
        ArgumentNullException.ThrowIfNull(canonicalIds);

        var wanted = canonicalIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();

        if (wanted.Length == 0)
        {
            return [];
        }

        return (await Session
            .Query<KnowledgeObject, KnowledgeObjectIndex>(x => x.Source == dataSourceId && x.CanonicalId.IsIn(wanted), collection: CollectionName)
            .ListAsync(cancellationToken)).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<KnowledgeObject>> GetByRootIdAsync(string dataSourceId, string rootId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataSourceId);
        ArgumentException.ThrowIfNullOrEmpty(rootId);

        return (await Session
            .Query<KnowledgeObject, KnowledgeObjectIndex>(x => x.Source == dataSourceId && x.RootId == rootId, collection: CollectionName)
            .ListAsync(cancellationToken)).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<KnowledgeObject>> GetByDataSourceIdAsync(string dataSourceId, CancellationToken cancellationToken = default)
    {
        return await GetAsync(dataSourceId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<KnowledgeObject>> GetByStatusAsync(string dataSourceId, string status, int take, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataSourceId);
        ArgumentException.ThrowIfNullOrEmpty(status);

        return (await Session
            .Query<KnowledgeObject, KnowledgeObjectIndex>(x => x.Source == dataSourceId && x.Status == status, collection: CollectionName)
            .Take(take < 1 ? 1 : take)
            .ListAsync(cancellationToken)).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<KnowledgeObject>> GetByObjectTypesAsync(string dataSourceId, IEnumerable<string> objectTypes, int take, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataSourceId);

        var wanted = objectTypes?
            .Where(objectType => !string.IsNullOrWhiteSpace(objectType))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];

        // The object type is indexed alongside the source, so both the kinds and the bound are applied by the
        // database. A listing has no query to rank against, which is precisely why it must never become a
        // scan of the data source: it is the one call nothing else narrows.
        var query = wanted.Length > 0
            ? Session.Query<KnowledgeObject, KnowledgeObjectIndex>(x => x.Source == dataSourceId && x.ObjectType.IsIn(wanted), collection: CollectionName)
            : Session.Query<KnowledgeObject, KnowledgeObjectIndex>(x => x.Source == dataSourceId, collection: CollectionName);

        // Ordered by the canonical identifier so a bounded listing returns the same objects every time rather
        // than whatever the database happened to hand back first.
        return (await query
            .OrderBy(x => x.CanonicalId)
            .Take(take < 1 ? 1 : take)
            .ListAsync(cancellationToken)).ToArray();
    }

    /// <inheritdoc />
    public async Task<KnowledgeObject> FindFigureByContentHashAsync(string contentHash, string promptVersion, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(contentHash);

        // The lookup crosses data sources on purpose: the same artwork is the same artwork wherever it was
        // ingested, and transcribing it twice buys nothing. The hash and the object type are both indexed, so
        // the database returns the handful of objects made from these bytes rather than every figure in the
        // installation.
        var candidates = await Session
            .Query<KnowledgeObject, KnowledgeObjectIndex>(x => x.ContentHash == contentHash && x.ObjectType.IsIn(_figureTypes), collection: CollectionName)
            .ListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            if (IsDescribedFigure(candidate, contentHash, promptVersion))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task DeleteByRootIdAsync(string dataSourceId, string rootId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataSourceId);
        ArgumentException.ThrowIfNullOrEmpty(rootId);

        var entries = await Session
            .Query<KnowledgeObject, KnowledgeObjectIndex>(x => x.Source == dataSourceId && x.RootId == rootId, collection: CollectionName)
            .ListAsync(cancellationToken);

        foreach (var entry in entries)
        {
            Session.Delete(entry, CollectionName);
        }
    }

    /// <summary>
    /// Determines whether an object is a figure already transcribed from the same bytes by the same prompt.
    /// </summary>
    /// <param name="entry">The object.</param>
    /// <param name="contentHash">The hash of the bytes.</param>
    /// <param name="promptVersion">The transcription prompt version.</param>
    /// <returns><see langword="true"/> when the description can be reused.</returns>
    /// <remarks>
    /// The type and the hash are re-checked against the document even though the query already narrowed on the
    /// index columns, because the database compares strings with its own collation and a case-insensitive one
    /// would hand back bytes that are not these bytes.
    /// </remarks>
    private static bool IsDescribedFigure(KnowledgeObject entry, string contentHash, string promptVersion)
    {
        if (entry.ObjectType is not (KnowledgeObjectTypes.Figure or KnowledgeObjectTypes.Chart))
        {
            return false;
        }

        if (!string.Equals(entry.ContentHash, contentHash, StringComparison.Ordinal))
        {
            return false;
        }

        if (!entry.TryGet<FigureDetails>(out var details) || string.IsNullOrWhiteSpace(details.Description))
        {
            return false;
        }

        return string.IsNullOrEmpty(promptVersion) ||
            string.Equals(details.DescriptionPromptVersion, promptVersion, StringComparison.Ordinal);
    }
}
