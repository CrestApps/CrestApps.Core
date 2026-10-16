using System.Text.Json.Nodes;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;

namespace CrestApps.Core.Blazor.Web.ViewModels;

/// <summary>
/// The form model of the AI persona create and edit pages.
/// </summary>
public sealed class AIPersonaViewModel
{
    /// <summary>
    /// Gets or sets the identifier of the persona being edited, or <see langword="null"/> for a new one.
    /// </summary>
    public string ItemId { get; set; }

    /// <summary>
    /// Gets or sets the technical name profiles and chat interactions link to. It cannot change after creation.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string DisplayText { get; set; }

    /// <summary>
    /// Gets or sets an optional description.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the persona instructions, a Liquid template placed before the system message.
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
    /// Gets or sets the deployment the voice was chosen from.
    /// </summary>
    public string VoiceDeploymentName { get; set; }

    /// <summary>
    /// Gets or sets the voice the persona speaks with.
    /// </summary>
    public string VoiceName { get; set; }

    /// <summary>
    /// Gets or sets an optional avatar image URL.
    /// </summary>
    public string AvatarUrl { get; set; }

    /// <summary>
    /// Gets or sets whether the persona comes from a persona file rather than the catalog. Saving such a persona
    /// creates a saved persona with the same name, which replaces the file.
    /// </summary>
    public bool IsFileBased { get; set; }

    /// <summary>
    /// Creates the form model of a persona.
    /// </summary>
    /// <param name="persona">The persona to edit.</param>
    /// <param name="isFileBased">Whether the persona comes from a persona file.</param>
    public static AIPersonaViewModel FromPersona(AIPersona persona, bool isFileBased = false)
    {
        ArgumentNullException.ThrowIfNull(persona);

        return new AIPersonaViewModel
        {
            ItemId = persona.ItemId,
            Name = persona.Name,
            DisplayText = persona.DisplayText,
            Description = persona.Description,
            Instructions = persona.Instructions,
            AssistantName = persona.AssistantName,
            Language = persona.Language,
            VoiceDeploymentName = persona.VoiceDeploymentName,
            VoiceName = persona.VoiceName,
            AvatarUrl = persona.AvatarUrl,
            IsFileBased = isFileBased,
        };
    }

    /// <summary>
    /// Builds the data passed to <c>IAIPersonaManager.NewAsync</c> or <c>UpdateAsync</c>. Every field is present,
    /// so a field cleared on the form is cleared on the persona. The technical name is only read on create.
    /// </summary>
    public JsonObject ToJson()
    {
        var voiceName = Normalize(VoiceName);

        return new JsonObject
        {
            [nameof(AIPersona.Name)] = Normalize(Name),
            [nameof(AIPersona.DisplayText)] = Normalize(DisplayText),
            [nameof(AIPersona.Description)] = Normalize(Description),
            [nameof(AIPersona.Instructions)] = Normalize(Instructions),
            [nameof(AIPersona.AssistantName)] = Normalize(AssistantName),
            [nameof(AIPersona.Language)] = Normalize(Language),
            [nameof(AIPersona.VoiceName)] = voiceName,

            // The deployment only qualifies the voice, so it means nothing without one.
            [nameof(AIPersona.VoiceDeploymentName)] = voiceName is null ? null : Normalize(VoiceDeploymentName),
            [nameof(AIPersona.AvatarUrl)] = Normalize(AvatarUrl),
        };
    }

    /// <summary>
    /// Gets the identifiers of the personas that come from persona files rather than the catalog.
    /// </summary>
    /// <param name="providers">The persona providers, or <see langword="null"/> when personas are not registered.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static async Task<HashSet<string>> GetFileBasedIdsAsync(IEnumerable<IAIPersonaProvider> providers, CancellationToken cancellationToken = default)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in providers ?? [])
        {
            foreach (var persona in await provider.GetPersonasAsync(cancellationToken))
            {
                if (!string.IsNullOrEmpty(persona?.ItemId))
                {
                    ids.Add(persona.ItemId);
                }
            }
        }

        return ids;
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
