using CrestApps.Core.Models;
using CrestApps.Core.Services;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// A reusable character an AI profile, chat interaction or workflow task links to: who the assistant is, how it
/// talks, what it calls itself and which voice it speaks with. A persona is linked by its name rather than copied,
/// so an edit applies to everything that uses it from the next message.
/// </summary>
public sealed class AIPersona : CatalogItem, INameAwareModel, IDisplayTextAwareModel, IModifiedUtcAwareModel, ICloneable<AIPersona>
{
    /// <summary>
    /// Gets or sets the technical name. Profiles and interactions link to the persona by this name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string DisplayText { get; set; }

    /// <summary>
    /// Gets or sets an optional description of the persona.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the persona instructions: the personality, tone and manners. They are rendered with Liquid and
    /// placed before the system message of everything that links to the persona.
    /// </summary>
    public string Instructions { get; set; }

    /// <summary>
    /// Gets or sets the name the assistant uses for itself.
    /// </summary>
    public string AssistantName { get; set; }

    /// <summary>
    /// Gets or sets the language the persona speaks, as a BCP-47 tag such as <c>en-US</c>.
    /// </summary>
    public string Language { get; set; }

    /// <summary>
    /// Gets or sets the provider-specific voice the persona speaks with.
    /// </summary>
    public string VoiceName { get; set; }

    /// <summary>
    /// Gets or sets the name of the deployment the voice was chosen from. A voice belongs to one provider, so it is
    /// used only on deployments of the same provider.
    /// </summary>
    public string VoiceDeploymentName { get; set; }

    /// <summary>
    /// Gets or sets an optional avatar image URL.
    /// </summary>
    public string AvatarUrl { get; set; }

    /// <summary>
    /// Gets or sets when the persona was created.
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Gets or sets when the persona was last modified.
    /// </summary>
    public DateTime? ModifiedUtc { get; set; }

    /// <summary>
    /// Gets or sets the name of the user who created the persona.
    /// </summary>
    public string Author { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user who owns the persona.
    /// </summary>
    public string OwnerId { get; set; }

    /// <summary>
    /// Creates a copy of the persona.
    /// </summary>
    public AIPersona Clone()
    {
        return new AIPersona
        {
            ItemId = ItemId,
            Name = Name,
            DisplayText = DisplayText,
            Description = Description,
            Instructions = Instructions,
            AssistantName = AssistantName,
            Language = Language,
            VoiceName = VoiceName,
            VoiceDeploymentName = VoiceDeploymentName,
            AvatarUrl = AvatarUrl,
            CreatedUtc = CreatedUtc,
            ModifiedUtc = ModifiedUtc,
            Author = Author,
            OwnerId = OwnerId,
            Properties = new Dictionary<string, object>(Properties, StringComparer.OrdinalIgnoreCase),
        };
    }
}
