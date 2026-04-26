using CrestApps.Core.Models;
using CrestApps.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.Data.EntityCore.Services;

public class SourceDocumentCatalog<T> : DocumentCatalog<T>, ISourceCatalog<T>
    where T : CatalogItem, ISourceAwareModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SourceDocumentCatalog"/> class.
    /// </summary>
    /// <param name="dbContext">The db context.</param>
    /// <param name="logger">The logger.</param>
    public SourceDocumentCatalog(
        CrestAppsEntityDbContext dbContext,
        ILogger<DocumentCatalog<T>> logger = null)
        : base(dbContext, logger)
    {
    }

    /// <summary>
    /// Gets the operation.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<IReadOnlyCollection<T>> GetAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);

        var records = await GetReadQuery()
            .Where(x => x.Source == source)
            .ToListAsync(cancellationToken);

return records
            .Select(CatalogRecordFactory.Materialize<T>)
            .ToArray();
    }
}
