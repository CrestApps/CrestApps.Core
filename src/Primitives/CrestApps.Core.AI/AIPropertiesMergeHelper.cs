using System.Text.Json.Nodes;
using CrestApps.Core.Support;

namespace CrestApps.Core.AI;

internal static class AIPropertiesMergeHelper
{
    public static void Merge(JsonObject target, JsonObject source)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (source is null)
        {
            return;
        }

        foreach (var (key, value) in source)
        {
            switch (value)
            {
                case null:
                    target[key] = null;
                    break;
                case JsonObject sourceObject when target[key] is JsonObject targetObject:
                    Merge(targetObject, sourceObject);
                    break;
                case JsonObject sourceObject:
                    target[key] = sourceObject.Clone();
                    break;
                case JsonArray sourceArray:
                    target[key] = sourceArray.DeepClone();
                    break;
                default:
                    target[key] = value.DeepClone();
                    break;
            }
        }
    }

    public static void MergeNamedEntries(JsonObject current, JsonObject existingSnapshot)
    {
        if (current is null || existingSnapshot is null)
        {
            return;
        }

        foreach (var (key, currentValue) in current)
        {
            if (!existingSnapshot.TryGetPropertyValue(key, out var existingValue))
            {
                continue;
            }

            if (currentValue is JsonObject currentObject && existingValue is JsonObject existingObject)
            {
                MergeNamedEntries(currentObject, existingObject);

                continue;
            }

            if (currentValue is not JsonArray currentArray || existingValue is not JsonArray existingArray)
            {
                continue;
            }

            if (!TryGetNamedEntries(currentArray, out var currentEntries) ||
                !TryGetNamedEntries(existingArray, out var existingEntries))
            {
                continue;
            }

            foreach (var (name, (index, currentEntry)) in currentEntries)
            {
                if (!existingEntries.TryGetValue(name, out var existingEntry))
                {
                    continue;
                }

                var existingEntrySnapshot = existingEntry.entry.Clone();
                Merge(existingEntry.entry, currentEntry);
                MergeNamedEntries(existingEntry.entry, existingEntrySnapshot);
                currentArray[index] = existingEntry.entry.Clone();
            }
        }
    }

    private static bool TryGetNamedEntries(
        JsonArray array,
        out Dictionary<string, (int index, JsonObject entry)> entries)
    {
        entries = [];

        for (var index = 0; index < array.Count; index++)
        {
            if (array[index] is not JsonObject entry)
            {
                entries = null;

                return false;
            }

            if (!entry.TryGetTrimmedStringValue("Name", out var name) ||
                string.IsNullOrWhiteSpace(name))
            {
                entries = null;

                return false;
            }

            entries[name] = (index, entry);
        }

        return entries.Count > 0;
    }
}
