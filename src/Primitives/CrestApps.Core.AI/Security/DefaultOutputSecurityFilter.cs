using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Default implementation of <see cref="IOutputSecurityFilter"/> that scans AI-generated
/// responses for system prompt leaks, tool disclosure, sensitive data exposure, and unsafe content.
/// </summary>
public sealed partial class DefaultOutputSecurityFilter : IOutputSecurityFilter
{
    private readonly IAIChatSecurityAuditService _auditService;
    private readonly IOptions<PromptSecurityOptions> _options;
    private readonly ILogger<DefaultOutputSecurityFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultOutputSecurityFilter"/> class.
    /// </summary>
    /// <param name="auditService">The security audit service.</param>
    /// <param name="options">The prompt security options.</param>
    /// <param name="logger">The logger.</param>
    public DefaultOutputSecurityFilter(
        IAIChatSecurityAuditService auditService,
        IOptions<PromptSecurityOptions> options,
        ILogger<DefaultOutputSecurityFilter> logger)
    {
        _auditService = auditService;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Validates AI-generated output for security concerns.
    /// </summary>
    /// <param name="context">The output security context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<PromptSecurityResult> ValidateOutputAsync(OutputSecurityContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = _options.Value;

        if (!options.EnableOutputFiltering)
        {
            return PromptSecurityResult.Safe;
        }

        if (string.IsNullOrWhiteSpace(context.Output))
        {
            return PromptSecurityResult.Safe;
        }

        // Check for system prompt leakage (most critical — exact match of system prompt content).
        var leakResult = DetectSystemPromptLeak(context);

        if (leakResult.IsBlocked)
        {
            await LogAndAuditAsync(leakResult, context, cancellationToken);

            return leakResult;
        }

        // Check for tool/function schema disclosure in output.
        var toolResult = DetectToolSchemaDisclosure(context.Output);

        if (toolResult.RiskLevel != PromptRiskLevel.None)
        {
            await LogAndAuditAsync(toolResult, context, cancellationToken);

            return toolResult;
        }

        // Check for sensitive data patterns in output.
        var piiResult = DetectSensitiveDataExposure(context.Output);

        if (piiResult.RiskLevel != PromptRiskLevel.None)
        {
            await LogAndAuditAsync(piiResult, context, cancellationToken);

            return piiResult;
        }

        // Check for XSS/script injection patterns in output.
        var xssResult = DetectUnsafeOutputPatterns(context.Output);

        if (xssResult.RiskLevel != PromptRiskLevel.None)
        {
            await LogAndAuditAsync(xssResult, context, cancellationToken);

            return xssResult;
        }

        // Return lower-severity findings (disclosure indicators).
        if (leakResult.RiskLevel != PromptRiskLevel.None)
        {
            await LogAndAuditAsync(leakResult, context, cancellationToken);

            return leakResult;
        }

        return PromptSecurityResult.Safe;
    }

    private async Task LogAndAuditAsync(PromptSecurityResult result, OutputSecurityContext context, CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Output security event: Rule={Rule}, Risk={RiskLevel}, Session={SessionId}",
            result.DetectionRule,
            result.RiskLevel,
            context.SessionId);

        if (_options.Value.EnableAuditLogging)
        {
            await _auditService.RecordOutputEventAsync(context, result, cancellationToken);
        }
    }

    private static PromptSecurityResult DetectSystemPromptLeak(OutputSecurityContext context)
    {
        if (string.IsNullOrWhiteSpace(context.SystemMessage))
        {
            return CheckDisclosureIndicatorsOnly(context.Output);
        }

        var output = context.Output;
        var systemMessage = context.SystemMessage;

        // Look for substantial substring matches from the system message in the output.
        const int minLeakLength = 50;
        var systemLines = systemMessage.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in systemLines)
        {
            if (line.Length < minLeakLength)
            {
                continue;
            }

            if (output.Contains(line, StringComparison.OrdinalIgnoreCase))
            {
                return PromptSecurityResult.Blocked(
                    "Detected system prompt content in AI response.",
                    PromptRiskLevel.Critical,
                    "SystemPromptLeak");
            }
        }

        return CheckDisclosureIndicatorsOnly(output);
    }

    private static PromptSecurityResult CheckDisclosureIndicatorsOnly(string output)
    {
        if (ContainsDisclosureIndicator(output))
        {
            return PromptSecurityResult.Flagged(
                "AI response may contain system prompt disclosure indicators.",
                PromptRiskLevel.Medium,
                "DisclosureIndicator");
        }

        return PromptSecurityResult.Safe;
    }

    private static PromptSecurityResult DetectToolSchemaDisclosure(string output)
    {
        // Detect patterns that indicate the model disclosed its internal tool/function schemas.
        // This catches the attack from the audit where the model outputted "namespace functions {"
        // with full tool definitions.
        ReadOnlySpan<string> toolSchemaIndicators =
        [
            "namespace functions {",
            "namespace functions{",
            "type generate_",
            "}) => any;",
            "}) => any",
            "## functions",
            "# Tools",
        ];

        var matchCount = 0;

        foreach (var indicator in toolSchemaIndicators)
        {
            if (output.Contains(indicator, StringComparison.OrdinalIgnoreCase))
            {
                matchCount++;
            }
        }

        // Require at least 2 indicators to avoid false positives (e.g., discussing tool schemas in general).
        if (matchCount >= 2)
        {
            return PromptSecurityResult.Blocked(
                "Detected internal tool/function schema disclosure in AI response.",
                PromptRiskLevel.Critical,
                "ToolSchemaDisclosure");
        }

        // Check for structured function definition patterns.
        if (ToolDefinitionPatternRegex().IsMatch(output))
        {
            return PromptSecurityResult.Flagged(
                "AI response may contain tool definition patterns.",
                PromptRiskLevel.High,
                "ToolDefinitionPattern");
        }

        return PromptSecurityResult.Safe;
    }

    private static PromptSecurityResult DetectSensitiveDataExposure(string output)
    {
        // Detect PII patterns being echoed in output.
        // This catches attacks where the model is tricked into repeating sensitive data.
        if (SsnPatternRegex().IsMatch(output) && SensitiveDataContextRegex().IsMatch(output))
        {
            return PromptSecurityResult.Blocked(
                "Detected sensitive personal information (SSN pattern) in AI response.",
                PromptRiskLevel.Critical,
                "SensitiveDataExposure");
        }

        if (CreditCardPatternRegex().IsMatch(output) && SensitiveDataContextRegex().IsMatch(output))
        {
            return PromptSecurityResult.Blocked(
                "Detected sensitive financial information (credit card pattern) in AI response.",
                PromptRiskLevel.Critical,
                "SensitiveDataExposure");
        }

        return PromptSecurityResult.Safe;
    }

    private static PromptSecurityResult DetectUnsafeOutputPatterns(string output)
    {
        // Detect XSS/script injection payloads in output that could cause harm
        // if rendered in a browser context.
        if (XssPayloadRegex().IsMatch(output))
        {
            return PromptSecurityResult.Flagged(
                "AI response contains potentially executable script content.",
                PromptRiskLevel.Medium,
                "UnsafeOutputContent");
        }

        return PromptSecurityResult.Safe;
    }

    private static bool ContainsDisclosureIndicator(string output)
    {
        ReadOnlySpan<string> indicators =
        [
            "my system prompt is",
            "my instructions are",
            "my system message says",
            "i was instructed to",
            "here is my system prompt",
            "here are my instructions",
            "my initial prompt",
            "the system prompt reads",
            "my hidden instructions",
            "my secret instructions",
            "the system prompt i have is",
            "now, the system prompt i have is",
            "my system prompt contains",
            "the instructions provided to me",
            "the exact text of the instructions",
            "my developer instructions",
        ];

        foreach (var indicator in indicators)
        {
            if (output.Contains(indicator, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(
        @"type\s+\w+\s*=\s*\(_\s*:\s*\{[^}]*\}\)\s*=>\s*any",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex ToolDefinitionPatternRegex();

    [GeneratedRegex(
        @"\b\d{3}[-\s]?\d{2}[-\s]?\d{4}\b",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex SsnPatternRegex();

    [GeneratedRegex(
        @"\b(?:4\d{3}|5[1-5]\d{2}|3[47]\d{2}|6(?:011|5\d{2}))[-\s]?\d{4}[-\s]?\d{4}[-\s]?\d{3,4}\b",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex CreditCardPatternRegex();

    [GeneratedRegex(
        @"\b(?:stored|provided|shared|confidential|sensitive|financial|personal)\b.{0,40}\b(?:information|data|number|ssn|account|credit\s+card)\b|\b(?:account\s+number|ssn|social\s+security|credit\s+card)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex SensitiveDataContextRegex();

    [GeneratedRegex(
        @"<script\b[^>]*>[\s\S]*?</script>|javascript\s*:\s*\S|on(?:error|load|click|mouseover|focus|blur)\s*=\s*[""'][^""']*[""']|\beval\s*\(\s*[""']",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex XssPayloadRegex();
}
