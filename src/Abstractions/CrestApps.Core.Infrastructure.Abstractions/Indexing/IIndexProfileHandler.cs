using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.Infrastructure.Indexing;

public interface IIndexProfileHandler : ICatalogEntryHandler<SearchIndexProfile>
{
    /// <summary>
    /// Validates a search index profile before it is persisted or synchronized.
    /// </summary>
    /// <param name="indexProfile">The profile being validated.</param>
    /// <param name="result">The validation result collector to populate with errors or warnings.</param>
    /// <param name="cancellationToken">A token that cancels validation work.</param>
    /// <returns>A value task that completes when validation has finished.</returns>
    ValueTask ValidateAsync(
        SearchIndexProfile indexProfile,
        ValidationResultDetails result,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the provider-specific field definitions required to create or update the remote index for the profile.
    /// </summary>
    /// <param name="indexProfile">The profile whose fields should be generated.</param>
    /// <param name="cancellationToken">A token that cancels field generation.</param>
    /// <returns>A value task whose result contains the field definitions for the profile.</returns>
    ValueTask<IReadOnlyCollection<SearchIndexField>> GetFieldsAsync(
        SearchIndexProfile indexProfile,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Called after the profile has been synchronized to the remote index provider.
    /// </summary>
    /// <param name="indexProfile">The profile that completed synchronization.</param>
    /// <param name="cancellationToken">A token that cancels post-synchronization work.</param>
    /// <returns>A task that completes when post-synchronization work has finished.</returns>
    Task SynchronizedAsync(SearchIndexProfile indexProfile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets provider-specific state associated with the profile before rebuilding or reprovisioning it.
    /// </summary>
    /// <param name="indexProfile">The profile being reset.</param>
    /// <param name="cancellationToken">A token that cancels the reset operation.</param>
    /// <returns>A task that completes when the profile state has been reset.</returns>
    Task ResetAsync(SearchIndexProfile indexProfile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called before the profile is deleted so provider-specific cleanup can run.
    /// </summary>
    /// <param name="indexProfile">The profile being deleted.</param>
    /// <param name="cancellationToken">A token that cancels the delete preparation.</param>
    /// <returns>A task that completes when delete preparation has finished.</returns>
    Task DeletingAsync(SearchIndexProfile indexProfile, CancellationToken cancellationToken = default);
}
