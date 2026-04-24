using CrestApps.Core.Models;

namespace CrestApps.Core.Services;

/// <summary>
/// Handler invoked when a catalog entry is being created.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public interface ICatalogCreatingHandler<T> where T : class
{
    /// <summary>
    /// Called when a catalog entry is about to be created.
    /// </summary>
    /// <param name="context">The context containing the entry to create.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task CreatingAsync(CreatingContext<T> context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Handler invoked after a catalog entry has been created.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public interface ICatalogCreatedHandler<T> where T : class
{
    /// <summary>
    /// Called after a catalog entry has been created.
    /// </summary>
    /// <param name="context">The context containing the created entry.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task CreatedAsync(CreatedContext<T> context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Handler invoked when a catalog entry is being updated.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public interface ICatalogUpdatingHandler<T> where T : class
{
    /// <summary>
    /// Called when a catalog entry is about to be updated.
    /// </summary>
    /// <param name="context">The context containing the entry to update.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task UpdatingAsync(UpdatingContext<T> context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Handler invoked after a catalog entry has been updated.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public interface ICatalogUpdatedHandler<T> where T : class
{
    /// <summary>
    /// Called after a catalog entry has been updated.
    /// </summary>
    /// <param name="context">The context containing the updated entry.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task UpdatedAsync(UpdatedContext<T> context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Handler invoked when a catalog entry is being deleted.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public interface ICatalogDeletingHandler<T> where T : class
{
    /// <summary>
    /// Called when a catalog entry is about to be deleted.
    /// </summary>
    /// <param name="context">The context containing the entry to delete.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task DeletingAsync(DeletingContext<T> context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Handler invoked after a catalog entry has been deleted.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public interface ICatalogDeletedHandler<T> where T : class
{
    /// <summary>
    /// Called after a catalog entry has been deleted.
    /// </summary>
    /// <param name="context">The context containing the deleted entry.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task DeletedAsync(DeletedContext<T> context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Handler invoked when a catalog entry is being validated.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public interface ICatalogValidatingHandler<T> where T : class
{
    /// <summary>
    /// Called when a catalog entry is about to be validated.
    /// </summary>
    /// <param name="context">The context containing the entry to validate.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task ValidatingAsync(ValidatingContext<T> context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Handler invoked after a catalog entry has been validated.
/// </summary>
/// <typeparam name="T">The type of catalog entry.</typeparam>
public interface ICatalogValidatedHandler<T> where T : class
{
    /// <summary>
    /// Called after a catalog entry has been validated.
    /// </summary>
    /// <param name="context">The context containing the validated entry and any validation results.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    Task ValidatedAsync(ValidatedContext<T> context, CancellationToken cancellationToken = default);
}
