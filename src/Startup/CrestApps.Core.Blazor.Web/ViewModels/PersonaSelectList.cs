using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Blazor.Web.ViewModels;

/// <summary>
/// Builds the options of an AI persona picker.
/// </summary>
public static class PersonaSelectList
{
    /// <summary>
    /// Builds the persona options, sorted by display text. When the selected persona is not among
    /// <paramref name="personas"/>, it is added as a missing option so the link survives a save.
    /// </summary>
    /// <param name="personas">The personas to offer.</param>
    /// <param name="selectedPersonaId">The technical name of the persona currently linked, if any.</param>
    public static List<PersonaSelectionItem> Build(IEnumerable<AIPersona> personas, string selectedPersonaId)
    {
        var items = (personas ?? [])
            .Where(persona => persona is not null && !string.IsNullOrWhiteSpace(persona.Name))
            .GroupBy(persona => persona.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(persona => new PersonaSelectionItem
            {
                PersonaId = persona.Name,
                DisplayText = string.IsNullOrWhiteSpace(persona.DisplayText) ? persona.Name : persona.DisplayText,
                VoiceName = string.IsNullOrWhiteSpace(persona.VoiceName) ? null : persona.VoiceName,
            })
            .OrderBy(item => item.DisplayText, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(selectedPersonaId) &&
            !items.Any(item => string.Equals(item.PersonaId, selectedPersonaId, StringComparison.OrdinalIgnoreCase)))
        {
            items.Add(new PersonaSelectionItem
            {
                PersonaId = selectedPersonaId,
                DisplayText = $"{selectedPersonaId} (missing)",
                IsMissing = true,
            });
        }

        return items;
    }

    /// <summary>
    /// Normalizes a selected persona id: an empty selection means no persona.
    /// </summary>
    /// <param name="personaId">The selected value.</param>
    public static string NormalizePersonaId(string personaId)
    {
        return string.IsNullOrWhiteSpace(personaId) ? null : personaId.Trim();
    }
}
