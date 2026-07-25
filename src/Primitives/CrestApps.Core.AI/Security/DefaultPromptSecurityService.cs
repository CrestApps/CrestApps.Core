using CrestApps.Core.AI.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Security;

/// <summary>
/// Default implementation of <see cref="IPromptSecurityService"/> that validates user prompts
/// against known prompt injection patterns and configurable security policies.
/// Security guards are governed by the site-level <see cref="PromptSecurityOptions"/>, while the
/// injected rate limiter honors per-profile anti-spam throttle overrides.
/// </summary>
public sealed class DefaultPromptSecurityService : IPromptSecurityService
{
    private readonly PromptInjectionPatternDetector _detector;
    private readonly IChatRateLimiter _rateLimiter;
    private readonly IAIChatSecurityAuditService _auditService;
    private readonly IOptions<PromptSecurityOptions> _options;
    private readonly ILogger<DefaultPromptSecurityService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultPromptSecurityService"/> class.
    /// </summary>
    /// <param name="detector">The pattern detector.</param>
    /// <param name="rateLimiter">The chat rate limiter.</param>
    /// <param name="auditService">The security audit service.</param>
    /// <param name="options">The prompt security options.</param>
    /// <param name="logger">The logger.</param>
    public DefaultPromptSecurityService(
        PromptInjectionPatternDetector detector,
        IChatRateLimiter rateLimiter,
        IAIChatSecurityAuditService auditService,
        IOptions<PromptSecurityOptions> options,
        ILogger<DefaultPromptSecurityService> logger)
    {
        _detector = detector;
        _rateLimiter = rateLimiter;
        _auditService = auditService;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Validates a user prompt for potential security threats.
    /// </summary>
    /// <param name="context">The prompt security context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<PromptSecurityResult> ValidateInputAsync(PromptSecurityContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var siteOptions = _options.Value;

        // Rate limit check (before expensive regex evaluation).
        // The rate limiter honors per-profile anti-spam throttle overrides.
        var rateLimitResult = await _rateLimiter.EvaluateAsync(context, cancellationToken);

        if (rateLimitResult.IsThrottled)
        {
            var rateLimitBlockedResult = PromptSecurityResult.Blocked(
                $"Rate limit exceeded. Please wait {rateLimitResult.RetryAfterSeconds} second(s) before sending another message.",
                PromptRiskLevel.High,
                "rate-limit");

            if (siteOptions.EnableAuditLogging)
            {
                await _auditService.RecordInputEventAsync(context, rateLimitBlockedResult, cancellationToken);
            }

            return rateLimitBlockedResult;
        }

        var injectionDetectionEnabled = siteOptions.EnableInjectionDetection;

        if (!injectionDetectionEnabled)
        {
            return PromptSecurityResult.Safe;
        }

        if (string.IsNullOrWhiteSpace(context.Prompt))
        {
            return PromptSecurityResult.Safe;
        }

        var blockingThreshold = siteOptions.BlockingThreshold;
        var maxPromptLength = siteOptions.MaxPromptLength;
        var evaluationContext = new PromptSecurityEvaluationContext
        {
            OriginalInput = context.Prompt,
            MaxPromptLength = maxPromptLength,
            BlockingThreshold = blockingThreshold,
        };
        var result = await _detector.EvaluateAsync(evaluationContext, cancellationToken);

        if (result.RiskLevel != PromptRiskLevel.None)
        {
            _logger.LogWarning(
                "Prompt security event: Rule={Rule}, Risk={RiskLevel}, Score={Score}, Blocked={IsBlocked}, Rules={Rules}, Session={SessionId}",
                result.DetectionRule,
                result.RiskLevel,
                result.Score,
                result.IsBlocked,
                string.Join(", ", result.MatchedRuleIds),
                context.SessionId);

            var auditEnabled = siteOptions.EnableAuditLogging;

            if (auditEnabled)
            {
                await _auditService.RecordInputEventAsync(context, result, cancellationToken);
            }
        }

        return result;
    }
}
