using CrestApps.Core.Infrastructure.Indexing.Models;
using CrestApps.Core.Models;

namespace CrestApps.Core.Infrastructure.Indexing;

/// <summary>
/// Provisions a new search index based on a <see cref="SearchIndexProfile"/>,
/// validating the profile and creating the underlying index in the search backend.
/// </summary>
public interface ISearchIndexProfileProvisioningService
{
    /// <summary>
    /// Asynchronously creates the remote search index described by the specified profile.
    /// </summary>
    /// <param name="profile">The index profile that describes the index to create.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A <see cref="ValidationResultDetails"/> indicating whether provisioning succeeded.
    /// When provisioning fails, <see cref="ValidationResultDetails.Errors"/> contains the reported errors.
    /// </returns>
    Task<ValidationResultDetails> CreateAsync(SearchIndexProfile profile, CancellationToken cancellationToken = default);
}
