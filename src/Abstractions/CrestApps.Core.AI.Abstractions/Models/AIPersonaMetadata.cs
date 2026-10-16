namespace CrestApps.Core.AI.Models;

/// <summary>
/// Links an AI profile, chat interaction or profile template to one persona. The persona is
/// referenced by name rather than copied, so editing the persona changes everything that links to it.
/// </summary>
public sealed class AIPersonaMetadata
{
    /// <summary>
    /// Gets or sets the technical name of the linked persona, or <see langword="null"/> for none.
    /// </summary>
    public string PersonaId { get; set; }

    /// <summary>
    /// Gets or sets the Liquid arguments used when rendering the persona instructions.
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; }
}
