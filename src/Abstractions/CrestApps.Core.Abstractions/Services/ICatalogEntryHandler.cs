using CrestApps.Core.Models;

namespace CrestApps.Core.Services;

/// <summary>
/// Handles lifecycle events raised during catalog entry operations such as
/// initialization, validation, creation, update, and deletion. Implementations
/// can enrich entries, enforce business rules, or trigger side effects.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public interface ICatalogEntryHandler<T>
{
    /// <summary>
    /// Called when a catalog entry is being initialized with default values.
    /// </summary>
    /// <param name="context">The context containing the entry being initialized.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task InitializingAsync(InitializingContext<T> context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called after a catalog entry has been initialized with default values.
    /// </summary>
    /// <param name="context">The context containing the initialized entry.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task InitializedAsync(InitializedContext<T> context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called after a catalog entry has been loaded from the store.
    /// </summary>
    /// <param name="context">The context containing the loaded entry.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task LoadedAsync(LoadedContext<T> context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when a catalog entry is about to be validated.
    /// </summary>
    /// <param name="context">The context containing the entry to validate.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task ValidatingAsync(ValidatingContext<T> context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called after a catalog entry has been validated.
    /// </summary>
    /// <param name="context">The context containing the validated entry and any validation results.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task ValidatedAsync(ValidatedContext<T> context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when a catalog entry is about to be deleted.
    /// </summary>
    /// <param name="context">The context containing the entry to delete.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task DeletingAsync(DeletingContext<T> context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called after a catalog entry has been deleted.
    /// </summary>
    /// <param name="context">The context containing the deleted entry.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task DeletedAsync(DeletedContext<T> context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when a catalog entry is about to be updated.
    /// </summary>
    /// <param name="context">The context containing the entry to update.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task UpdatingAsync(UpdatingContext<T> context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called after a catalog entry has been updated.
    /// </summary>
    /// <param name="context">The context containing the updated entry.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task UpdatedAsync(UpdatedContext<T> context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when a catalog entry is about to be created.
    /// </summary>
    /// <param name="context">The context containing the entry to create.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task CreatingAsync(CreatingContext<T> context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called after a catalog entry has been created.
    /// </summary>
    /// <param name="context">The context containing the created entry.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task CreatedAsync(CreatedContext<T> context, CancellationToken cancellationToken = default);
}
