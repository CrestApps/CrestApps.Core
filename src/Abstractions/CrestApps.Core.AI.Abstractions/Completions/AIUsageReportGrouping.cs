namespace CrestApps.Core.AI.Completions;

/// <summary>
/// How an AI usage report groups metered requests. Every grouping keeps the provider, the model, and the kind of
/// request apart, because each combination is billed in its own units and at its own price.
/// </summary>
public enum AIUsageReportGrouping
{
    /// <summary>
    /// One row per provider, model, and kind of request.
    /// </summary>
    Model,

    /// <summary>
    /// One row per provider, model, kind of request, and category (<see cref="Models.AICompletionUsageRecord.ContextType"/>).
    /// </summary>
    ModelAndCategory,

    /// <summary>
    /// One row per provider, model, kind of request, and purpose (<see cref="Models.AICompletionUsageRecord.Purpose"/>).
    /// </summary>
    ModelAndPurpose,
}
