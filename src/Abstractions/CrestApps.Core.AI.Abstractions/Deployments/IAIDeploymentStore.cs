using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.AI.Deployments;

/// <summary>
/// Provides the merged runtime view of AI deployments across all registered binding
/// sources while preserving the standard named-and-sourced catalog operations used by
/// deployment managers and editors.
/// </summary>
public interface IAIDeploymentStore : INamedSourceCatalog<AIDeployment>
{
}
