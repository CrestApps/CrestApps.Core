using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Security;

/// <summary>
/// Default implementation of <see cref="IAIChatSecurityAuditService"/> that records
/// security events to the application log. Replace with a persistent store for
/// production forensics.
/// </summary>
public sealed class DefaultAIChatSecurityAuditService : IAIChatSecurityAuditService
{
    // Event ids let a log pipeline route audit events without matching the SECURITY_AUDIT text prefix.
    private static readonly EventId _inputEventId = new(2101, "SecurityAuditInput");
    private static readonly EventId _outputEventId = new(2102, "SecurityAuditOutput");

    private readonly ILogger<DefaultAIChatSecurityAuditService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAIChatSecurityAuditService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public DefaultAIChatSecurityAuditService(ILogger<DefaultAIChatSecurityAuditService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Records a security event when a prompt is blocked or flagged.
    /// </summary>
    /// <param name="context">The prompt security context.</param>
    /// <param name="result">The security evaluation result.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task RecordInputEventAsync(PromptSecurityContext context, PromptSecurityResult result, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            _inputEventId,
            "SECURITY_AUDIT: Input {Action} | Rule={Rule} | Risk={RiskLevel} | Score={Score} | Rules={Rules} | Session={SessionId} | Profile={ProfileId} | Connection={ConnectionId}",
            result.IsBlocked ? "BLOCKED" : "FLAGGED",
            result.DetectionRule,
            result.RiskLevel,
            result.Score,
            string.Join(", ", result.MatchedRuleIds),
            context.SessionId,
            context.ProfileId,
            context.ConnectionId);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Records a security event when an output is blocked or flagged.
    /// </summary>
    /// <param name="context">The output security context.</param>
    /// <param name="result">The security evaluation result.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task RecordOutputEventAsync(OutputSecurityContext context, PromptSecurityResult result, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            _outputEventId,
            "SECURITY_AUDIT: Output {Action} | Rule={Rule} | Risk={RiskLevel} | Score={Score} | Rules={Rules} | Session={SessionId}",
            result.IsBlocked ? "BLOCKED" : "FLAGGED",
            result.DetectionRule,
            result.RiskLevel,
            result.Score,
            string.Join(", ", result.MatchedRuleIds),
            context.SessionId);

        return Task.CompletedTask;
    }
}
