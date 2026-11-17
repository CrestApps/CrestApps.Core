using System.Text.Json;
using System.Text.Json.Nodes;
using CrestApps.Core.Support;

namespace CrestApps.Core.AI;

internal static class AIPropertiesMergeHelper
{
    /// <summary>
    /// Applies the <c>Properties</c> object of an incoming payload over an entity's stored properties, for the
    /// catalog handlers that map recipe, deployment and API JSON onto a record.
    /// </summary>
    /// <param name="entity">The entity whose stored properties are updated.</param>
    /// <param name="json">The incoming payload.</param>
    /// <remarks>
    /// An entry the payload leaves out keeps its stored value, at every level, so a partial update never erases
    /// a connection or a secret it did not mention. Keys match without regard to case, like the
    /// <see cref="ExtensibleEntity.Properties"/> dictionary itself. A top-level entry sent as <see langword="null"/>
    /// is removed; a nested field sent as <see langword="null"/> is cleared.
    /// </remarks>
    public static void MergeProperties(ExtensibleEntity entity, JsonObject json)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (!json.TryGetObjectValue(nameof(ExtensibleEntity.Properties), out var incoming) || incoming is null)
        {
            return;
        }

        var options = ExtensibleEntityExtensions.JsonSerializerOptions;
        var current = JsonExtensions.FromObject(entity.Properties ?? new Dictionary<string, object>(), options);

        foreach (var (incomingKey, value) in incoming)
        {
            var key = FindKeyIgnoringCase(current, incomingKey) ?? incomingKey;

            if (value is null)
            {
                current.Remove(key);

                continue;
            }

            if (value is JsonObject incomingObject && current[key] is JsonObject currentObject)
            {
                MergeIgnoringCase(currentObject, incomingObject);

                continue;
            }

            current[key] = value.DeepClone();
        }

        var merged = JsonSerializer.Deserialize<Dictionary<string, object>>(current, options);

        entity.Properties = merged is null
            ? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object>(merged, StringComparer.OrdinalIgnoreCase);
    }

    private static void MergeIgnoringCase(JsonObject target, JsonObject source)
    {
        foreach (var (sourceKey, value) in source)
        {
            var key = FindKeyIgnoringCase(target, sourceKey) ?? sourceKey;

            if (value is JsonObject sourceObject && target[key] is JsonObject targetObject)
            {
                MergeIgnoringCase(targetObject, sourceObject);

                continue;
            }

            target[key] = value?.DeepClone();
        }
    }

    private static string FindKeyIgnoringCase(JsonObject target, string key)
    {
        if (target.ContainsKey(key))
        {
            return key;
        }

        foreach (var (existingKey, _) in target)
        {
            if (string.Equals(existingKey, key, StringComparison.OrdinalIgnoreCase))
            {
                return existingKey;
            }
        }

        return null;
    }

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
