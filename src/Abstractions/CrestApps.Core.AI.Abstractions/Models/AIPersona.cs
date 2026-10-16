namespace CrestApps.Core.AI.Models;

/// <summary>
/// A persona resolved for one request: its instructions are already rendered and ready to place in the
/// system message.
/// </summary>
public sealed class AIPersona
{
    /// <summary>
    /// Gets the technical name of the persona template.
    /// </summary>
    public string Id { get; init; }

    /// <summary>
    /// Gets the display name of the persona template.
    /// </summary>
    public string DisplayText { get; init; }

    /// <summary>
    /// Gets the rendered persona instructions.
    /// </summary>
    public string Instructions { get; init; }

    /// <summary>
    /// Gets the name the assistant uses for itself.
    /// </summary>
    public string AssistantName { get; init; }

    /// <summary>
    /// Gets the language the persona speaks, as a BCP-47 tag.
    /// </summary>
    public string Language { get; init; }

    /// <summary>
    /// Gets the provider-specific voice the persona speaks with.
    /// </summary>
    public string VoiceName { get; init; }

    /// <summary>
    /// Gets the name of the deployment the voice was chosen from.
    /// </summary>
    public string VoiceDeploymentName { get; init; }

    /// <summary>
    /// Gets the optional avatar image URL.
    /// </summary>
    public string AvatarUrl { get; init; }
}
