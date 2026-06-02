namespace CrestApps.Core.AI.Security;

/// <summary>
/// Captures non-sensitive telemetry about how prompt security evaluation processed an input.
/// </summary>
public sealed class PromptSecurityDetectionTelemetry
{
    /// <summary>
    /// A shared empty telemetry instance.
    /// </summary>
    public static readonly PromptSecurityDetectionTelemetry Empty = new();

    /// <summary>
    /// Gets or sets the original input length before normalization.
    /// </summary>
    public int OriginalLength { get; set; }

    /// <summary>
    /// Gets or sets the normalized input length after Unicode cleanup and whitespace collapsing.
    /// </summary>
    public int NormalizedLength { get; set; }

    /// <summary>
    /// Gets or sets the folded input length after homoglyph folding.
    /// </summary>
    public int FoldedLength { get; set; }

    /// <summary>
    /// Gets or sets the number of zero-width or invisible characters removed during normalization.
    /// </summary>
    public int RemovedZeroWidthCharacterCount { get; set; }

    /// <summary>
    /// Gets or sets the number of whitespace runs collapsed during normalization.
    /// </summary>
    public int CollapsedWhitespaceRunCount { get; set; }

    /// <summary>
    /// Gets or sets the number of homoglyph substitutions applied during folding.
    /// </summary>
    public int HomoglyphReplacementCount { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Unicode normalization changed the input.
    /// </summary>
    public bool UnicodeNormalized { get; set; }

    /// <summary>
    /// Gets or sets the total number of matched rules.
    /// </summary>
    public int MatchedRuleCount { get; set; }

    /// <summary>
    /// Gets or sets the total number of distinct matched categories.
    /// </summary>
    public int DistinctCategoryCount { get; set; }

    /// <summary>
    /// Gets or sets the total evaluation duration in milliseconds.
    /// </summary>
    public double EvaluationDurationMilliseconds { get; set; }
}
