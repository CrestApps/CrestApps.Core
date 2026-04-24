using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Completions;

/// <summary>
/// Handles events raised during AI completion processing, such as when a message
/// or streaming update is received from the AI provider. Implementations can
/// perform logging, analytics, response enrichment, or other post-processing.
/// </summary>
public interface IAICompletionHandler
{
    /// <summary>
    /// Handles a received message asynchronously.
    /// </summary>
    /// <param name="context">The context containing details of the received message.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task ReceivedMessageAsync(ReceivedMessageContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles a received update asynchronously.
    /// </summary>
    /// <param name="context">The context containing details of the received update.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task ReceivedUpdateAsync(ReceivedUpdateContext context, CancellationToken cancellationToken = default);
}
