using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;

namespace CrestApps.Core.AI.Handlers;

/// <summary>
/// Core orchestration context handler that builds the <see cref="AICompletionContext"/>
/// from the resource using the existing <see cref="IAICompletionContextBuilder"/> pipeline,
/// and resolves the <see cref="OrchestrationContext.SourceName"/>.
/// </summary>
internal sealed class CompletionContextOrchestrationHandler : IOrchestrationContextBuilderHandler
{
    private readonly IAICompletionContextBuilder _completionContextBuilder;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompletionContextOrchestrationHandler"/> class.
    /// </summary>
    /// <param name="completionContextBuilder">The completion context builder.</param>
    public CompletionContextOrchestrationHandler(IAICompletionContextBuilder completionContextBuilder)
    {
        _completionContextBuilder = completionContextBuilder;
    }

    /// <summary>
    /// Buildings the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    public async Task BuildingAsync(OrchestrationContextBuildingContext context)
    {
        // Build the AICompletionContext using the existing handler pipeline.
        var completionContext = await _completionContextBuilder.BuildAsync(context.Resource);
        context.Context.CompletionContext = completionContext;

        // Propagate DisableTools from the completion context.
        context.Context.DisableTools = context.Context.CompletionContext.DisableTools;

        // Seed the SystemMessageBuilder with the initial system message.
        if (!string.IsNullOrEmpty(context.Context.CompletionContext.SystemMessage))
        {
            context.Context.SystemMessageBuilder.Append(context.Context.CompletionContext.SystemMessage);
        }
    }

    /// <summary>
    /// Builts the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    public Task BuiltAsync(OrchestrationContextBuiltContext context)
    {
        return Task.CompletedTask;
    }
}
