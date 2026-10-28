namespace CrestApps.Core.AI.Guardrails;

/// <summary>
/// Told when a guardrail triggers, for example to start a workflow, raise an alert or record an audit entry.
/// </summary>
public interface IAIGuardrailHandler
{
    /// <summary>
    /// Called after a guardrail triggered, before the placeholder reply is sent.
    /// </summary>
    /// <param name="verdict">The guardrail that triggered.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the handler is done.</returns>
    Task TriggeredAsync(AIGuardrailVerdict verdict, CancellationToken cancellationToken);
}
