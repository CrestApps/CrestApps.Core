using CrestApps.Core.AI.Profiles;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CrestApps.Core.Mvc.Web.Services;

/// <summary>
/// Builds the options of the persona pickers. Personas are an opt-in feature, so a <see langword="null"/> manager
/// means the host did not add it and there is nothing to pick.
/// </summary>
internal static class AIPersonaSelectListBuilder
{
    /// <summary>
    /// Builds the persona options. A linked persona that no longer exists stays selectable, so saving the form does
    /// not drop the link silently.
    /// </summary>
    /// <param name="personaManager">The persona manager, or <see langword="null"/> when personas are not enabled.</param>
    /// <param name="selectedPersonaId">The technical name of the linked persona, if any.</param>
    public static async Task<List<SelectListItem>> BuildAsync(IAIPersonaManager personaManager, string selectedPersonaId)
    {
        var personas = personaManager is null
            ? []
            : await personaManager.GetAllAsync();

        var items = personas
            .OrderBy(persona => persona.DisplayText ?? persona.Name, StringComparer.OrdinalIgnoreCase)
            .Select(persona => new SelectListItem(
                string.IsNullOrWhiteSpace(persona.DisplayText) ? persona.Name : persona.DisplayText,
                persona.Name,
                string.Equals(persona.Name, selectedPersonaId, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (!string.IsNullOrWhiteSpace(selectedPersonaId) &&
            !items.Any(item => string.Equals(item.Value, selectedPersonaId, StringComparison.OrdinalIgnoreCase)))
        {
            items.Insert(0, new SelectListItem(selectedPersonaId, selectedPersonaId, true));
        }

        return items;
    }

    /// <summary>
    /// Maps each persona's technical name to its voice, for the "Use persona voice" hint of the voice pickers.
    /// </summary>
    /// <param name="personaManager">The persona manager, or <see langword="null"/> when personas are not enabled.</param>
    public static async Task<Dictionary<string, string>> GetVoicesAsync(IAIPersonaManager personaManager)
    {
        var voices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (personaManager is null)
        {
            return voices;
        }

        foreach (var persona in await personaManager.GetAllAsync())
        {
            if (!string.IsNullOrWhiteSpace(persona.Name) && !string.IsNullOrWhiteSpace(persona.VoiceName))
            {
                voices[persona.Name] = persona.VoiceName;
            }
        }

        return voices;
    }
}
