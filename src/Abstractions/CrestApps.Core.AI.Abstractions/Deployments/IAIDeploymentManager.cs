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
    /// Asynchronously retrieves all deployments supporting the specified capability.
    /// </summary>
    /// <param name="capability">The deployment capability to filter by.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A ValueTask that represents the asynchronous operation. The result is an <see cref="IEnumerable{AIDeployment}"/>
    /// containing all deployments matching the specified capability.
    /// </returns>
    ValueTask<IEnumerable<AIDeployment>> GetByCapabilityAsync(AIDeploymentCapability capability, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves all deployments supporting the specified legacy type.
    /// </summary>
    /// <param name="type">The deployment type to filter by.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    [Obsolete("Use GetByCapabilityAsync instead.")]
    ValueTask<IEnumerable<AIDeployment>> GetByTypeAsync(AIDeploymentType type, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the default deployment of a given capability for a specific client.
    /// Returns the deployment marked as IsDefault for that type on the client,
    /// or the first deployment supporting that type on the client if none is marked as default.
    /// </summary>
    /// <param name="clientName">The name of the client to resolve the default deployment for.</param>
    /// <param name="capability">The deployment capability to filter by.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    ValueTask<AIDeployment> GetDefaultAsync(string clientName, AIDeploymentCapability capability, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the default deployment of a given legacy type for a specific client.
    /// </summary>
    /// <param name="clientName">The name of the client to resolve the default deployment for.</param>
    /// <param name="type">The deployment type to filter by.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    [Obsolete("Use the capability overload instead.")]
    ValueTask<AIDeployment> GetDefaultAsync(string clientName, AIDeploymentType type, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a deployment using the full fallback chain:
    /// 1. If deploymentId is provided, returns that specific deployment.
    /// 2. Falls back to the global default deployment for the given capability (from DefaultAIDeploymentSettings).
    /// 3. Falls back to the first deployment supporting the requested capability within the current scope.
    /// Returns <see langword="null"/> if no deployment can be resolved.
    /// </summary>
    /// <param name="capability">The deployment capability to resolve.</param>
    /// <param name="deploymentName">The optional deployment name to look up directly.</param>
    /// <param name="clientName">The optional client name to scope the resolution.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    ValueTask<AIDeployment> ResolveOrDefaultAsync(AIDeploymentCapability capability, string deploymentName = null, string clientName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a deployment using the full fallback chain for a legacy type.
    /// </summary>
    /// <param name="type">The deployment type to resolve.</param>
    /// <param name="deploymentName">The optional deployment name to look up directly.</param>
    /// <param name="clientName">The optional client name to scope the resolution.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    [Obsolete("Use the capability overload instead.")]
    ValueTask<AIDeployment> ResolveOrDefaultAsync(AIDeploymentType type, string deploymentName = null, string clientName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all deployments of a given capability, optionally filtered by client.
    /// Results are suitable for dropdown population, grouped by connection.
    /// </summary>
    /// <param name="capability">The deployment capability to filter by.</param>
    /// <param name="clientName">The optional client name to further filter results.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    ValueTask<IEnumerable<AIDeployment>> GetAllByCapabilityAsync(AIDeploymentCapability capability, string clientName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all deployments of a given legacy type, optionally filtered by client.
    /// </summary>
    /// <param name="type">The deployment type to filter by.</param>
    /// <param name="clientName">The optional client name to further filter results.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    [Obsolete("Use GetAllByCapabilityAsync instead.")]
    ValueTask<IEnumerable<AIDeployment>> GetAllByTypeAsync(AIDeploymentType type, string clientName = null, CancellationToken cancellationToken = default);
}
