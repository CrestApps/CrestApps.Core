using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.ResponseHandling;
using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// The built-in AI chat response handler that routes prompts through the orchestration pipeline.
/// This is the default handler used when no custom <see cref="IChatResponseHandler"/> is configured.
/// </summary>
public sealed class AIChatResponseHandler : IChatResponseHandler
{
    /// <summary>
    /// The well-known name for the built-in AI handler.
    /// </summary>
    public const string HandlerName = ChatResponseHandlerNames.AI;

    /// <summary>
    /// Gets the name.
    /// </summary>
    public string Name => HandlerName;

    /// <summary>
    /// Handles the operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<ChatResponseHandlerResult> HandleAsync(
        ChatResponseHandlerContext context,
        CancellationToken cancellationToken = default)
    {
        var orchestrationContextBuilder = context.Services.GetRequiredService<IOrchestrationContextBuilder>();
        var orchestratorResolver = context.Services.GetRequiredService<IOrchestratorResolver>();

        OrchestrationContext orchestratorContext;
        string orchestratorName;

        if (context.ChatType == ChatContextType.AIChatSession)
        {
            // Store the session in the invocation context before the build, so profile providers and orchestration
            // handlers that run during the build can resolve the session (for example, an agent that only a given
            // session offers), and document tools can resolve the session documents later.
            AIInvocationScope.Current.ChatSession = context.ChatSession;
            AIInvocationScope.Current.Items[nameof(AIChatSession)] = context.ChatSession;

            orchestratorContext = await orchestrationContextBuilder.BuildAsync(
                context.Profile,
                ctx =>
                {
                    ctx.UserMessage = context.Prompt;
                    ctx.ConversationHistory = context.ConversationHistory;
                    ctx.CompletionContext.AdditionalProperties[AICompletionContextKeys.Session] = context.ChatSession;
                },
                cancellationToken);

            orchestratorName = context.Profile.OrchestratorName;

            AIInvocationScope.Current.CompletionContext = orchestratorContext.CompletionContext;
            AIInvocationScope.Current.DataSourceId = orchestratorContext.CompletionContext.DataSourceId;
        }
        else
        {
            // Store the interaction in the invocation context before the build, for the same reason as the session.
            AIInvocationScope.Current.ChatInteraction = context.Interaction;

            orchestratorContext = await orchestrationContextBuilder.BuildAsync(
                context.Interaction,
                ctx =>
                {
                    ctx.UserMessage = context.Prompt;
                    ctx.ConversationHistory = context.ConversationHistory;
                },
                cancellationToken);

            orchestratorName = context.Interaction.OrchestratorName;

            AIInvocationScope.Current.CompletionContext = orchestratorContext.CompletionContext;
            AIInvocationScope.Current.DataSourceId = orchestratorContext.CompletionContext.DataSourceId;
        }

        context.Properties["OrchestrationContext"] = orchestratorContext;

        var orchestrator = orchestratorResolver.Resolve(orchestratorName);
        var stream = orchestrator.ExecuteStreamingAsync(orchestratorContext, cancellationToken);

        return ChatResponseHandlerResult.Streaming(stream);
    }
}
