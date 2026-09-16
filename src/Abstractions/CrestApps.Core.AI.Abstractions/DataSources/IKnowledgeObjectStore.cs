using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.AI.DataSources;

/// <summary>
/// Persists the typed knowledge objects produced by ingesting files into an AI data source.
/// </summary>
/// <remarks>
/// The store is authoritative for the objects themselves. The knowledge-base index is a projection of them,
/// rebuilt by a full synchronization, so an index that is lost or recreated costs a re-index rather than a
/// re-ingest. Records are grouped by the owning data source, stored as their source.
/// </remarks>
public interface IKnowledgeObjectStore : ISourceCatalog<KnowledgeObject>
{
    /// <summary>
    /// Finds one object by its canonical identifier.
    /// </summary>
    /// <param name="dataSourceId">The owning data source.</param>
    /// <param name="canonicalId">The canonical identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The object, or <see langword="null"/> when there is none.</returns>
    Task<KnowledgeObject> FindByCanonicalIdAsync(string dataSourceId, string canonicalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the objects with the supplied canonical identifiers.
    /// </summary>
    /// <param name="dataSourceId">The owning data source.</param>
    /// <param name="canonicalIds">The canonical identifiers.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The objects that exist.</returns>
    Task<IReadOnlyCollection<KnowledgeObject>> GetByCanonicalIdsAsync(string dataSourceId, IEnumerable<string> canonicalIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets every object belonging to one ingested document.
    /// </summary>
    /// <param name="dataSourceId">The owning data source.</param>
    /// <param name="rootId">The document's canonical identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The objects.</returns>
    Task<IReadOnlyCollection<KnowledgeObject>> GetByRootIdAsync(string dataSourceId, string rootId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets every object in a data source.
    /// </summary>
    /// <param name="dataSourceId">The owning data source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The objects.</returns>
    Task<IReadOnlyCollection<KnowledgeObject>> GetByDataSourceIdAsync(string dataSourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the objects in a data source that are in the supplied state.
    /// </summary>
    /// <param name="dataSourceId">The owning data source.</param>
    /// <param name="status">The state to look for. See <see cref="KnowledgeObjectStatus"/>.</param>
    /// <param name="take">The most objects to return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The objects.</returns>
    Task<IReadOnlyCollection<KnowledgeObject>> GetByStatusAsync(string dataSourceId, string status, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the objects in a data source that are of the supplied kinds.
    /// </summary>
    /// <param name="dataSourceId">The owning data source.</param>
    /// <param name="objectTypes">
    /// The kinds to return. See <see cref="CrestApps.Core.Infrastructure.Indexing.KnowledgeObjectTypes"/>.
    /// An empty set returns every kind.
    /// </param>
    /// <param name="take">The most objects to return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The objects, ordered by canonical identifier so the same call returns the same ones.</returns>
    /// <remarks>
    /// This is what lets a caller enumerate a data source - every figure, every table - rather than only rank
    /// it against a query. A question about the set cannot be answered by similarity, and it must not be
    /// answered by reading the data source into memory either: both shipped stores apply the kinds and the
    /// bound in the database, because the object type is indexed alongside the source.
    /// <para>
    /// It is default-implemented over <see cref="GetByDataSourceIdAsync"/> so a store outside this repository
    /// keeps compiling. That fallback materializes the data source and filters it in memory, which is exactly
    /// what an implementation should override to avoid.
    /// </para>
    /// </remarks>
    async Task<IReadOnlyCollection<KnowledgeObject>> GetByObjectTypesAsync(
        string dataSourceId,
        IEnumerable<string> objectTypes,
        int take,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataSourceId);

        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (objectTypes is not null)
        {
            foreach (var objectType in objectTypes)
            {
                if (!string.IsNullOrWhiteSpace(objectType))
                {
                    wanted.Add(objectType);
                }
            }
        }

        var objects = await GetByDataSourceIdAsync(dataSourceId, cancellationToken);

        return objects
            .Where(entry => wanted.Count == 0 || wanted.Contains(entry.ObjectType))
            .OrderBy(entry => entry.CanonicalId, StringComparer.Ordinal)
            .Take(take < 1 ? 1 : take)
            .ToArray();
    }

    /// <summary>
    /// Finds a figure that was already transcribed from identical bytes by the same prompt.
    /// </summary>
    /// <param name="contentHash">The hash of the figure bytes.</param>
    /// <param name="promptVersion">The transcription prompt version.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The figure, or <see langword="null"/> when nothing matches.</returns>
    /// <remarks>
    /// This is what stops the same artwork being sent to a vision model again on every re-ingest, and it
    /// works across documents and across data sources because it keys on the bytes.
    /// </remarks>
    Task<KnowledgeObject> FindFigureByContentHashAsync(string contentHash, string promptVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every object belonging to one ingested document.
    /// </summary>
    /// <param name="dataSourceId">The owning data source.</param>
    /// <param name="rootId">The document's canonical identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task DeleteByRootIdAsync(string dataSourceId, string rootId, CancellationToken cancellationToken = default);
}
