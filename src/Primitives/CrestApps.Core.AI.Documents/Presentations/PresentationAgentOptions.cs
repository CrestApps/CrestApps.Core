namespace CrestApps.Core.AI.Documents.Presentations;

/// <summary>
/// Options for the system presentation agent.
/// </summary>
public sealed class PresentationAgentOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the presentation agent is offered to the model. Turning it off
    /// keeps PowerPoint reading and writing but stops decks from being built and edited in conversation.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
