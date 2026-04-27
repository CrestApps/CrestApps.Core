using CrestApps.Core.AI.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.AI.Connections;

/// <summary>
/// Provides the merged runtime view of AI provider connections across all registered
/// binding sources while preserving the standard named-and-sourced catalog operations
/// used by connection managers and editors.
/// </summary>
public interface IAIProviderConnectionStore : INamedSourceCatalog<AIProviderConnection>
{
}
