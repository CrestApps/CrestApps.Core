using CrestApps.Core.AI.Models;

namespace CrestApps.Core.Blazor.Web.ViewModels;

/// <summary>
/// Builds the options of an AI persona picker.
/// </summary>
public static class PersonaSelectList
{
    /// <summary>
    /// Builds the persona options, sorted by category and then by display text. When the selected persona is
    /// not among <paramref name="personas"/>, it is added as a missing option so the link survives a save.
    /// </summary>
    /// <param name="personas">The persona templates to offer.</param>
    /// <param name="selectedPersonaId">The technical name of the persona currently linked, if any.</param>
    public static List<PersonaSelectionItem> Build(IEnumerable<AIProfileTemplate> personas, string selectedPersonaId)
    {
        var items = (personas ?? [])
            .Where(persona => persona is not null && !string.IsNullOrWhiteSpace(persona.Name))
            .GroupBy(persona => persona.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(persona =>
            {
                persona.TryGet<PersonaTemplateMetadata>(out var metadata);

                return new PersonaSelectionItem
                {
                    PersonaId = persona.Name,
                    DisplayText = string.IsNullOrWhiteSpace(persona.DisplayText) ? persona.Name : persona.DisplayText,
                    Category = string.IsNullOrWhiteSpace(persona.Category) ? null : persona.Category.Trim(),
                    VoiceName = string.IsNullOrWhiteSpace(metadata?.VoiceName) ? null : metadata.VoiceName,
                };
            })
            .OrderBy(item => item.Category ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.DisplayText, StringComparer.OrdinalIgnoreCase)
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
