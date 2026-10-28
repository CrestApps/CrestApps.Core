using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.Guardrails;

/// <summary>
/// A guardrail that triggered.
/// </summary>
public sealed class AIGuardrailVerdict
{
    /// <summary>
    /// Gets or sets the AI profile.
    /// </summary>
    public string ProfileId { get; set; }

    /// <summary>
    /// Gets or sets the chat session, when the conversation has one.
    /// </summary>
    public string SessionId { get; set; }

    /// <summary>
    /// Gets or sets which side was checked.
    /// </summary>
    public AIGuardrailDirection Direction { get; set; }

    /// <summary>
    /// Gets or sets the topic's template identifier, or <see cref="AIGuardrailTopics.Other"/> for the profile's own
    /// description of other topics.
    /// </summary>
    public string Category { get; set; }

    /// <summary>
    /// Gets or sets the topic's title.
    /// </summary>
    public string Topic { get; set; }

    /// <summary>
    /// Gets or sets the utility model's one-sentence reason.
    /// </summary>
    public string Reason { get; set; }

    /// <summary>
    /// Gets or sets the text that was checked.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Gets or sets what the AI says instead.
    /// </summary>
    public string Reply { get; set; }
}
