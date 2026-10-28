namespace CrestApps.Core.AI.Guardrails;

/// <summary>
/// Which side of the conversation a guardrail checked.
/// </summary>
public enum AIGuardrailDirection
{
    /// <summary>
    /// What the customer said, before the AI answers it.
    /// </summary>
    Customer,

    /// <summary>
    /// The AI's reply, before it is sent or spoken.
    /// </summary>
    Reply,
}
