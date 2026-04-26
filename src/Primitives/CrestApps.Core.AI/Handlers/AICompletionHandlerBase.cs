using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Handlers;

/// <summary>
/// Represents the AI Completion Handler Base.
/// </summary>
public abstract class AICompletionHandlerBase : IAICompletionHandler
{
    /// <summary>
    /// Received messages message.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public virtual Task ReceivedMessageAsync(ReceivedMessageContext context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Receiveds update.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public virtual Task ReceivedUpdateAsync(ReceivedUpdateContext context, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
