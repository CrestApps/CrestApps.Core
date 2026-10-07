using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;

namespace CrestApps.Core.AI.Documents.Word.Services;

/// <summary>
/// Gives the Word agent every one of its tools on every request.
/// </summary>
/// <remarks>
/// The agent has more tools than the orchestrator's scoping threshold, so without this the orchestrator keeps only
/// the tools whose names and descriptions share the most words with the request. A request such as "switch to the
/// modern theme and make the headings navy" then loses <c>format_word_document</c>, the one tool that does it, and
/// the agent spends its tool rounds trying to restyle the document paragraph by paragraph. Its system prompt names
/// every tool and when to use it, so the full set is what it is written for.
/// </remarks>
internal sealed class WordAgentToolScopeHandler : IOrchestrationContextBuilderHandler
{
    /// <summary>
    /// Runs before the context is built. Nothing is needed here.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task BuildingAsync(OrchestrationContextBuildingContext context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Keeps every Word tool in scope when the context is built for the Word agent.
    /// </summary>
    /// <param name="context">The built context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task BuiltAsync(OrchestrationContextBuiltContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Resource is AIProfile profile &&
            string.Equals(profile.Name, WordAgentProvider.AgentName, StringComparison.Ordinal) &&
            !context.OrchestrationContext.DisableTools)
        {
            context.OrchestrationContext.MustIncludeTools.AddRange(WordAgentProvider.ToolNames);
        }

        return Task.CompletedTask;
    }
}
