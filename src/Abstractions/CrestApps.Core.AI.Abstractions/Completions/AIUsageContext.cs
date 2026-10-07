namespace CrestApps.Core.AI.Completions;

/// <summary>
/// The labels <see cref="AIUsageScope"/> applies to the AI usage recorded within it.
/// </summary>
public sealed class AIUsageContext
{
    /// <summary>
    /// Gets the category recorded in <see cref="Models.AICompletionUsageRecord.ContextType"/>.
    /// </summary>
    public string ContextType { get; init; }

    /// <summary>
    /// Gets the purpose recorded in <see cref="Models.AICompletionUsageRecord.Purpose"/>.
    /// </summary>
    public string Purpose { get; init; }
}
