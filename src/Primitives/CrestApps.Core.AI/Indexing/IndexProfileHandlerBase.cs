using CrestApps.Core.Handlers;
using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;

namespace CrestApps.Core.AI.Indexing;

/// <summary>
/// Represents the index Profile Handler Base.
/// </summary>
public abstract class IndexProfileHandlerBase : CatalogEntryHandlerBase<SearchIndexProfile>, IIndexProfileHandler
{
    /// <summary>
    /// Validates the operation.
    /// </summary>
    /// <param name="indexProfile">The index profile.</param>
    /// <param name="result">The result.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public virtual ValueTask ValidateAsync(SearchIndexProfile indexProfile, ValidationResultDetails result, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Gets fields.
    /// </summary>
    /// <param name="indexProfile">The index profile.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public virtual ValueTask<IReadOnlyCollection<SearchIndexField>> GetFieldsAsync(SearchIndexProfile indexProfile, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<IReadOnlyCollection<SearchIndexField>>(null);
    }

    /// <summary>
    /// Synchronizeds the operation.
    /// </summary>
    /// <param name="indexProfile">The index profile.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public virtual Task SynchronizedAsync(SearchIndexProfile indexProfile, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Resets the operation.
    /// </summary>
    /// <param name="indexProfile">The index profile.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public virtual Task ResetAsync(SearchIndexProfile indexProfile, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Deletings the operation.
    /// </summary>
    /// <param name="indexProfile">The index profile.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public virtual Task DeletingAsync(SearchIndexProfile indexProfile, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Validatings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task ValidatingAsync(ValidatingContext<SearchIndexProfile> context, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(context.Model, context.Result, cancellationToken);
    }

    /// <summary>
    /// Deletings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public override async Task DeletingAsync(DeletingContext<SearchIndexProfile> context, CancellationToken cancellationToken = default)
    {
        await DeletingAsync(context.Model, cancellationToken);
    }
}
