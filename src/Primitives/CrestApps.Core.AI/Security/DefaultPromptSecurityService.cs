using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Default implementation of <see cref="IPromptSecurityService"/> that validates user prompts
/// against known prompt injection patterns and configurable security policies.
/// Respects per-profile security settings that override site-level defaults.
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

        // Resolve per-profile security settings.
        var profileSettings = context.Profile?.TryGetSettings<PromptSecurityProfileSettings>(out var ps) == true ? ps : null;

        // Check if security is entirely disabled for this profile.
        if (profileSettings?.IsEnabled == false)
        {
            return PromptSecurityResult.Safe;
        }

        // Rate limit check (before expensive regex evaluation).
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

        var injectionDetectionEnabled = profileSettings?.EnableInjectionDetection ?? siteOptions.EnableInjectionDetection;

        if (!injectionDetectionEnabled)
        {
            return PromptSecurityResult.Safe;
        }

        if (string.IsNullOrWhiteSpace(context.Prompt))
        {
            return PromptSecurityResult.Safe;
        }

        var blockingThreshold = profileSettings?.BlockingThreshold ?? siteOptions.BlockingThreshold;
        var maxPromptLength = profileSettings?.MaxPromptLength ?? siteOptions.MaxPromptLength;
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
