namespace CrestApps.Core.AI.Models;

/// <summary>
/// Metadata for templates with a "Persona" source. A persona describes who the assistant is and how it
/// speaks; the profile or interaction that links to it still decides what the assistant does.
/// Stored in the template's <see cref="ExtensibleEntity.Properties"/> via
/// <c>Put&lt;PersonaTemplateMetadata&gt;</c> / <c>As&lt;PersonaTemplateMetadata&gt;</c>.
/// </summary>
public sealed class PersonaTemplateMetadata
{
    /// <summary>
    /// Gets or sets the persona instructions. They are rendered as a Liquid template and placed at the top of
    /// the system message of every request made by a profile or interaction that uses the persona.
    /// </summary>
    public string Instructions { get; set; }

    /// <summary>
    /// Gets or sets the name the assistant uses for itself. Hosts may also show it on assistant messages.
    /// </summary>
    public string AssistantName { get; set; }

    /// <summary>
    /// Gets or sets the language the persona speaks, as a BCP-47 tag such as <c>en-US</c>. It is used as the
    /// speech language when the caller does not name one.
    /// </summary>
    public string Language { get; set; }

    /// <summary>
    /// Gets or sets the provider-specific voice the persona speaks with.
    /// </summary>
    public string VoiceName { get; set; }

    /// <summary>
    /// Gets or sets the name of the deployment <see cref="VoiceName"/> was chosen from. Voice names belong to a
    /// provider, so the voice is only used on a deployment of the same provider; when this is empty the voice is
    /// used everywhere.
    /// </summary>
    public string VoiceDeploymentName { get; set; }

    /// <summary>
    /// Gets or sets an optional avatar image URL that hosts may show next to assistant messages.
    /// </summary>
    public string AvatarUrl { get; set; }
}
