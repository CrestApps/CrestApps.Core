using CrestApps.Core.AI;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CrestApps.Core.Mvc.Web.Services;

/// <summary>
/// Builds the persona drop-down shown on the AI profile and profile template editors.
/// </summary>
internal static class AIPersonaSelectListBuilder
{
    /// <summary>
    /// Lists the listable persona templates, grouped by category. A selected persona that is no longer listed is
    /// still offered so saving the editor does not silently unlink it.
    /// </summary>
    /// <param name="templateManager">The manager that merges saved and file-based templates.</param>
    /// <param name="selectedPersonaId">The technical name of the persona the entity links to, if any.</param>
    public static async Task<List<SelectListItem>> BuildAsync(IAIProfileTemplateManager templateManager, string selectedPersonaId)
    {
        ArgumentNullException.ThrowIfNull(templateManager);

        var personas = await templateManager.GetAsync(AITemplateSources.Persona);
        var groups = new Dictionary<string, SelectListGroup>(StringComparer.OrdinalIgnoreCase);

        var items = personas
            .Where(template => template.IsListable ||
                string.Equals(template.Name, selectedPersonaId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(template => template.Category ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(template => template.DisplayText ?? template.Name, StringComparer.OrdinalIgnoreCase)
            .Select(template =>
            {
                SelectListGroup group = null;

                if (!string.IsNullOrWhiteSpace(template.Category) && !groups.TryGetValue(template.Category, out group))
                {
                    group = new SelectListGroup { Name = template.Category };
                    groups[template.Category] = group;
                }

                return new SelectListItem(
                    string.IsNullOrWhiteSpace(template.DisplayText) ? template.Name : template.DisplayText,
                    template.Name,
                    string.Equals(template.Name, selectedPersonaId, StringComparison.OrdinalIgnoreCase))
                {
                    Group = group,
                };
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(selectedPersonaId) &&
            !items.Any(item => string.Equals(item.Value, selectedPersonaId, StringComparison.OrdinalIgnoreCase)))
        {
            items.Insert(0, new SelectListItem(selectedPersonaId, selectedPersonaId, true));
        }

        return items;
    }

    /// <summary>
    /// Maps each persona's technical name to its voice, so a voice picker can say which voice an empty selection
    /// falls back to.
    /// </summary>
    /// <param name="templateManager">The manager that merges saved and file-based templates.</param>
    public static async Task<Dictionary<string, string>> GetVoicesAsync(IAIProfileTemplateManager templateManager)
    {
        ArgumentNullException.ThrowIfNull(templateManager);

        var personas = await templateManager.GetAsync(AITemplateSources.Persona);
        var voices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var template in personas)
        {
            if (string.IsNullOrWhiteSpace(template.Name))
            {
                continue;
            }

            var voiceName = template.GetOrCreate<PersonaTemplateMetadata>().VoiceName;

            if (!string.IsNullOrWhiteSpace(voiceName))
            {
                voices[template.Name] = voiceName;
            }
        }

        return voices;
    }
}
