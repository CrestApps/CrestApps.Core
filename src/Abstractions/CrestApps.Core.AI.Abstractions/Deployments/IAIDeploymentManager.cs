using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.AI.Deployments;

/// <summary>
/// Manages AI deployments with CRUD operations, composite name/source lookup,
/// type-filtered retrieval, and a multi-level fallback resolution chain for
/// selecting the appropriate deployment for a given request.
/// </summary>
public interface IAIDeploymentManager : INamedSourceCatalogManager<AIDeployment>
{
    /// <summary>
    /// Asynchronously retrieves a list of model deployments for the specified client.
    /// </summary>
    /// <param name="clientName">The name of the client. Must not be null or empty.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A ValueTask that represents the asynchronous operation. The result is an <see cref="IEnumerable{AIDeployment}"/>
    /// containing the model deployments for the specified client.
    /// </returns>
    ValueTask<IEnumerable<AIDeployment>> GetAllAsync(string clientName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the deployment that fills a named slot, walking a single ordered chain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The chain is, for the slot and then for each of its fallback slots in turn: the explicitly requested
    /// deployment name, then the slot's site-wide default deployment name. Only once every link is
    /// exhausted does it fall back to the first deployment capable of the terminal slot's required feature.
    /// </para>
    /// <para>
    /// The single chain matters. Resolving the utility slot and the chat slot independently and joining them
    /// with <c>??</c> lets the utility resolve's own "first capable" tail answer first, which silently routes
    /// background work such as summarization, data extraction, and query rewriting to an arbitrary model
    /// instead of the profile's configured chat deployment.
    /// </para>
    /// </remarks>
    /// <param name="slotName">The technical name of the slot. See <see cref="Models.AIDeploymentSlotNames"/>.</param>
    /// <param name="deploymentName">The optional deployment name explicitly requested for this slot.</param>
    /// <param name="clientName">The optional client name used to scope the "first capable" fallback.</param>
    /// <param name="fallbackDeploymentNames">
    /// The optional deployment names explicitly requested for the fallback slots, keyed by slot name. For
    /// the utility slot this carries the caller's chat deployment name.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    ValueTask<AIDeployment> ResolveSlotAsync(
        string slotName,
        string deploymentName = null,
        string clientName = null,
        IReadOnlyDictionary<string, string> fallbackDeploymentNames = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all deployments that qualify for a named slot, optionally filtered by client.
    /// Results are suitable for dropdown population.
    /// </summary>
    /// <param name="slotName">The technical name of the slot. See <see cref="Models.AIDeploymentSlotNames"/>.</param>
    /// <param name="clientName">The optional client name to further filter results.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    ValueTask<IEnumerable<AIDeployment>> GetAllBySlotAsync(string slotName, string clientName = null, CancellationToken cancellationToken = default);

}
