using System.Text.Json.Nodes;
using CrestApps.Core.AI.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CrestApps.Core.Mvc.Web.Areas.AI.ViewModels;

/// <summary>
/// The persona editor form.
/// </summary>
public sealed class AIPersonaViewModel
{
    /// <summary>
    /// Gets or sets the identifier of the saved persona, or <see langword="null"/> for a new one.
    /// </summary>
    public string ItemId { get; set; }

    /// <summary>
    /// Gets or sets the technical name profiles and interactions link to.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string DisplayText { get; set; }

    /// <summary>
    /// Gets or sets the optional description.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the persona instructions.
    /// </summary>
    public string Instructions { get; set; }

    /// <summary>
    /// Gets or sets the name the assistant uses for itself.
    /// </summary>
    public string AssistantName { get; set; }

    /// <summary>
    /// Gets or sets the language the persona speaks.
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
    /// Gets or sets the optional avatar image URL.
    /// </summary>
    public string AvatarUrl { get; set; }

    /// <summary>
    /// Gets or sets whether the persona comes from a file rather than the catalog. Saving it creates a saved persona
    /// with the same name, which replaces the file.
    /// </summary>
    [BindNever]
    public bool IsFromFile { get; set; }

    /// <summary>
    /// Gets or sets the deployments a voice can be picked from.
    /// </summary>
    [BindNever]
    public IEnumerable<SelectListItem> VoiceDeployments { get; set; } = [];

    /// <summary>
    /// Gets or sets the names of the realtime deployments, whose voices come from a different endpoint.
    /// </summary>
    [BindNever]
    public string[] RealtimeDeploymentNames { get; set; } = [];

    /// <summary>
    /// Builds the form from a persona.
    /// </summary>
    /// <param name="persona">The persona.</param>
    /// <param name="isFromFile">Whether the persona comes from a file.</param>
    public static AIPersonaViewModel FromPersona(AIPersona persona, bool isFromFile)
    {
        ArgumentNullException.ThrowIfNull(persona);

        return new AIPersonaViewModel
        {
            ItemId = isFromFile ? null : persona.ItemId,
            Name = persona.Name,
            DisplayText = persona.DisplayText,
            Description = persona.Description,
            Instructions = persona.Instructions,
            AssistantName = persona.AssistantName,
            Language = persona.Language,
            VoiceDeploymentName = persona.VoiceDeploymentName,
            VoiceName = persona.VoiceName,
            AvatarUrl = persona.AvatarUrl,
            IsFromFile = isFromFile,
        };
    }

    /// <summary>
    /// Builds the data the persona manager fills a persona from. Empty fields are sent as empty strings, so clearing
    /// a field in the form clears it on the persona.
    /// </summary>
    public JsonObject ToJson()
    {
        var voiceName = VoiceName?.Trim() ?? string.Empty;

        return new JsonObject
        {
            [nameof(AIPersona.Name)] = Name?.Trim(),
            [nameof(AIPersona.DisplayText)] = DisplayText?.Trim() ?? string.Empty,
            [nameof(AIPersona.Description)] = Description?.Trim() ?? string.Empty,
            [nameof(AIPersona.Instructions)] = Instructions ?? string.Empty,
            [nameof(AIPersona.AssistantName)] = AssistantName?.Trim() ?? string.Empty,
            [nameof(AIPersona.Language)] = Language?.Trim() ?? string.Empty,
            [nameof(AIPersona.VoiceName)] = voiceName,

            // The deployment only qualifies a voice; without one there is nothing to qualify.
            [nameof(AIPersona.VoiceDeploymentName)] = voiceName.Length == 0 ? string.Empty : VoiceDeploymentName?.Trim() ?? string.Empty,
            [nameof(AIPersona.AvatarUrl)] = AvatarUrl?.Trim() ?? string.Empty,
        };
    }
}
