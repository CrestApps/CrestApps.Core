namespace CrestApps.Core.AI.Security;

/// <summary>
/// Provides the evaluation inputs used by the prompt security detector and individual rules.
/// </summary>
public sealed class PromptSecurityEvaluationContext
{
    /// <summary>
    /// Gets or sets the original prompt text before normalization.
    /// </summary>
    public string OriginalInput { get; set; }

    /// <summary>
    /// Gets or sets the normalized prompt text used for primary regex evaluation.
    /// </summary>
    public string NormalizedInput { get; set; }

    /// <summary>
    /// Gets or sets the folded prompt text used to detect common homoglyph-based obfuscation.
    /// </summary>
    public string FoldedInput { get; set; }

    /// <summary>
    /// Gets or sets the maximum prompt length allowed for the current evaluation.
    /// </summary>
    public int MaxPromptLength { get; set; }

    /// <summary>
    /// Gets or sets the effective risk threshold at which the prompt should be blocked.
    /// </summary>
    public PromptRiskLevel BlockingThreshold { get; set; } = PromptRiskLevel.High;

    /// <summary>
    /// Gets or sets the evaluation telemetry gathered during normalization and rule execution.
    /// </summary>
    public PromptSecurityDetectionTelemetry Telemetry { get; set; } = PromptSecurityDetectionTelemetry.Empty;
}
