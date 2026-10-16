namespace CrestApps.Core.AI;

/// <summary>
/// Well-known template source identifiers.
/// </summary>
public static class AITemplateSources
{
    /// <summary>
    /// Templates designed to create AI Profiles.
    /// </summary>
    public const string Profile = "Profile";

    /// <summary>
    /// Templates designed to provide reusable system prompts.
    /// </summary>
    public const string SystemPrompt = "SystemPrompt";

    /// <summary>
    /// Templates that describe a reusable AI persona: who the assistant is, how it speaks and which
    /// voice it uses. AI profiles and chat interactions link to one by its name.
    /// </summary>
    public const string Persona = "Persona";
}
