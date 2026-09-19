using CrestApps.Core.AI.Models;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Materializes scoped <see cref="ToolRegistryEntry"/> instances into the concrete <see cref="AITool"/>
/// set handed to a model. This is the single shared implementation of "scoped entries → tools" used by
/// both the text completion path (function-invocation completion handler) and the realtime session
/// configurator, so tool selection, per-user authorization, and de-duplication behave identically
/// regardless of how the model is ultimately invoked.
/// </summary>
public interface IToolMaterializer
{
    /// <summary>
    /// Resolves the given scoped entries into <see cref="AITool"/> instances.
    /// </summary>
    /// <param name="entries">The already-scoped tool registry entries to materialize.</param>
    /// <param name="options">Controls per-user access enforcement.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<ToolMaterializationResult> MaterializeAsync(
        IReadOnlyList<ToolRegistryEntry> entries,
        ToolMaterializationOptions options,
        CancellationToken cancellationToken = default);
}
