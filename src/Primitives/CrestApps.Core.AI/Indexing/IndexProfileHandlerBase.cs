using CrestApps.Core.Infrastructure.Indexing;
using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Handlers;
using CrestApps.Core.Models;

namespace CrestApps.Core.AI.Indexing;

/// <summary>
/// Represents the index Profile Handler Base.
/// </summary>
public abstract class IndexProfileHandlerBase : CatalogEntryHandlerBase<SearchIndexProfile>, IIndexProfileHandler
{
    public virtual ValueTask ValidateAsync(SearchIndexProfile indexProfile, ValidationResultDetails result, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public virtual ValueTask<IReadOnlyCollection<SearchIndexField>> GetFieldsAsync(SearchIndexProfile indexProfile, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<IReadOnlyCollection<SearchIndexField>>(null);
    }

    public virtual Task SynchronizedAsync(SearchIndexProfile indexProfile, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public virtual Task ResetAsync(SearchIndexProfile indexProfile, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public virtual Task DeletingAsync(SearchIndexProfile indexProfile, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public override async Task ValidatingAsync(ValidatingContext<SearchIndexProfile> context, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(context.Model, context.Result, cancellationToken);
    }

    public override async Task DeletingAsync(DeletingContext<SearchIndexProfile> context, CancellationToken cancellationToken = default)
    {
        await DeletingAsync(context.Model, cancellationToken);
    }
}
