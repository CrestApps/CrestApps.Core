using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Orchestration;

/// <summary>
/// Handles lifecycle events raised while building an <see cref="OrchestrationContext"/>.
/// </summary>
/// <remarks>
/// Implementations can enrich, validate, or otherwise mutate the context. The builder will invoke
/// <see cref="BuildingAsync(OrchestrationContextBuildingContext)"/> first, then apply any caller-provided
/// configuration, and finally invoke <see cref="BuiltAsync(OrchestrationContextBuiltContext)"/>.
/// Handlers are resolved from DI and executed in reverse registration order to allow last-registered
/// handlers to run first.
/// </remarks>
public interface IOrchestrationContextBuilderHandler
{
    /// <summary>
    /// Called while the <see cref="OrchestrationContext"/> is being constructed, before the optional caller
    /// configuration delegate is applied.
    /// </summary>
    /// <param name="context">Carries both the source resource and the mutable <see cref="OrchestrationContext"/>.</param>
    /// <param name="cancellationToken">The cancellation token for the build operation.</param>
    /// <returns>A task that completes when the mutation or validation is done.</returns>
    Task BuildingAsync(OrchestrationContextBuildingContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called after the context has been fully constructed and the optional caller configuration delegate
    /// has been applied.
    /// </summary>
    /// <param name="context">Carries the final <see cref="OrchestrationContext"/> along with the source resource.</param>
    /// <param name="cancellationToken">The cancellation token for the build operation.</param>
    /// <returns>A task that completes when post-build processing is done.</returns>
    Task BuiltAsync(OrchestrationContextBuiltContext context, CancellationToken cancellationToken = default);
}
