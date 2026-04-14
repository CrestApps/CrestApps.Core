using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Tooling;

namespace CrestApps.Core.AI.Handlers;
/// <summary>
/// Sets the <see cref = "AIToolExecutionContext"/> on the current <see cref = "AIInvocationScope"/>
/// after the orchestration context is fully built. This removes the need for individual
/// hubs (AIChatHub, ChatInteractionHub) to manually construct and store the context.
/// </summary>
internal sealed class AIToolExecutionContextOrchestrationHandler : IOrchestrationContextBuilderHandler
{
    public Task BuildingAsync(OrchestrationContextBuildingContext context)
    {
        return Task.CompletedTask;
    }

    public Task BuiltAsync(OrchestrationContextBuiltContext context)
    {
        var invocationContext = AIInvocationScope.Current;
        if (invocationContext is null)
        {
            return Task.CompletedTask;
        }

        invocationContext.ToolExecutionContext ??= new AIToolExecutionContext(context.Resource);
        invocationContext.ToolExecutionContext.ClientName = context.OrchestrationContext.SourceName;

        return Task.CompletedTask;
    }
}
