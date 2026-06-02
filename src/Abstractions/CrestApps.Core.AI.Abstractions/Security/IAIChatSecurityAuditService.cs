namespace CrestApps.Core.AI.Security;

/// <summary>
/// Records security-relevant events for audit trail and forensic investigation.
/// </summary>
public interface IAIChatSecurityAuditService
{
    /// <summary>
    /// Records a security event when a prompt is blocked or flagged.
    /// </summary>
    /// <param name="context">The prompt security context.</param>
    /// <param name="result">The security evaluation result.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task RecordInputEventAsync(PromptSecurityContext context, PromptSecurityResult result, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a security event when an output is blocked or flagged.
    /// </summary>
    /// <param name="context">The output security context.</param>
    /// <param name="result">The security evaluation result.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task RecordOutputEventAsync(OutputSecurityContext context, PromptSecurityResult result, CancellationToken cancellationToken = default);
}
