namespace CrestApps.Core.AI.Guardrails;

/// <summary>
/// The utility model's answer to a guardrail check.
/// </summary>
internal sealed class AIGuardrailJudgement
{
    /// <summary>
    /// Gets or sets whether the message falls under a topic that is not allowed.
    /// </summary>
    public bool Violates { get; set; }

    /// <summary>
    /// Gets or sets the topic.
    /// </summary>
    public string Category { get; set; }

    /// <summary>
    /// Gets or sets why, in a sentence.
    /// </summary>
    public string Reason { get; set; }
}
