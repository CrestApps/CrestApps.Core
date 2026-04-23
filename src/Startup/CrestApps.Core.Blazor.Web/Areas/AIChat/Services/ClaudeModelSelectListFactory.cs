using CrestApps.Core.AI.Claude.Models;

namespace CrestApps.Core.Blazor.Web.Areas.AIChat.Services;

internal static class ClaudeModelSelectListFactory
{
    public static List<KeyValuePair<string, string>> Build(
        IEnumerable<ClaudeModelInfo> models,
        params string[] fallbackModelIds)
    {
        var items = (models ?? [])
            .Where(model => !string.IsNullOrWhiteSpace(model.Id))
            .Select(model => new KeyValuePair<string, string>(
                string.IsNullOrWhiteSpace(model.Name) ? model.Id : model.Name,
                model.Id))
            .ToList();

        var knownIds = items
            .Select(item => item.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var modelId in fallbackModelIds ?? [])
        {
            if (string.IsNullOrWhiteSpace(modelId) || !knownIds.Add(modelId))
            {
                continue;
            }

            items.Add(new KeyValuePair<string, string>(modelId, modelId));
        }

        return items
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
