using System.Globalization;
using System.Text.Json.Nodes;

namespace CrestApps.Core.AI.Tooling.SharedState;

/// <summary>
/// Applies the <c>add</c>, <c>replace</c> and <c>remove</c> operations of a JSON Patch (RFC 6902) document to a
/// JSON object. The shared state of a chat run is changed with it.
/// </summary>
public static class JsonPatchApplier
{
    /// <summary>
    /// Applies the operations to a copy of the document and returns the copy. The document itself is not changed,
    /// so a failing operation leaves it as it was.
    /// </summary>
    /// <param name="document">The document to change.</param>
    /// <param name="operations">The JSON Patch operations.</param>
    /// <returns>The changed copy of the document.</returns>
    /// <exception cref="JsonPatchException">An operation is not valid for the document.</exception>
    public static JsonObject Apply(JsonObject document, JsonArray operations)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var result = (document?.DeepClone() as JsonObject) ?? [];

        for (var index = 0; index < operations.Count; index++)
        {
            if (operations[index] is not JsonObject operation)
            {
                throw new JsonPatchException($"Operation {index} is not a JSON object.");
            }

            var op = GetString(operation, "op");
            var path = GetString(operation, "path") ?? throw new JsonPatchException($"Operation {index} has no path.");

            switch (op)
            {
                case "add":
                    RequireValue(operation, index);
                    Add(result, path, operation["value"]?.DeepClone());
                    break;

                case "replace":
                    RequireValue(operation, index);
                    Replace(result, path, operation["value"]?.DeepClone());
                    break;

                case "remove":
                    Remove(result, path);
                    break;

                default:
                    throw new JsonPatchException($"Operation {index} uses '{op}'. Only add, replace and remove are supported.");
            }
        }

        return result;
    }

    private static string GetString(JsonObject operation, string name)
    {
        return operation[name] is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;
    }

    private static void RequireValue(JsonObject operation, int index)
    {
        if (!operation.ContainsKey("value"))
        {
            throw new JsonPatchException($"Operation {index} has no value.");
        }
    }

    private static void Add(JsonObject root, string path, JsonNode value)
    {
        if (path.Length == 0)
        {
            throw new JsonPatchException("The whole state cannot be set by an add operation; use replace.");
        }

        var (parent, key) = ResolveParent(root, path);

        switch (parent)
        {
            case JsonObject obj:
                obj[key] = value;
                break;

            case JsonArray array:
                if (key == "-")
                {
                    array.Add(value);
                    break;
                }

                array.Insert(ParseIndex(key, array.Count, allowEnd: true), value);
                break;

            default:
                throw new JsonPatchException($"The parent of '{path}' is not an object or an array.");
        }
    }

    private static void Replace(JsonObject root, string path, JsonNode value)
    {
        if (path.Length == 0)
        {
            if (value is not JsonObject replacement)
            {
                throw new JsonPatchException("The state must stay a JSON object.");
            }

            root.Clear();

            foreach (var property in replacement.ToList())
            {
                replacement.Remove(property.Key);
                root[property.Key] = property.Value;
            }

            return;
        }

        var (parent, key) = ResolveParent(root, path);

        switch (parent)
        {
            case JsonObject obj:
                if (!obj.ContainsKey(key))
                {
                    throw new JsonPatchException($"'{path}' does not exist.");
                }

                obj[key] = value;
                break;

            case JsonArray array:
                array[ParseIndex(key, array.Count, allowEnd: false)] = value;
                break;

            default:
                throw new JsonPatchException($"The parent of '{path}' is not an object or an array.");
        }
    }

    private static void Remove(JsonObject root, string path)
    {
        if (path.Length == 0)
        {
            throw new JsonPatchException("The whole state cannot be removed.");
        }

        var (parent, key) = ResolveParent(root, path);

        switch (parent)
        {
            case JsonObject obj:
                if (!obj.Remove(key))
                {
                    throw new JsonPatchException($"'{path}' does not exist.");
                }

                break;

            case JsonArray array:
                array.RemoveAt(ParseIndex(key, array.Count, allowEnd: false));
                break;

            default:
                throw new JsonPatchException($"The parent of '{path}' is not an object or an array.");
        }
    }

    private static (JsonNode Parent, string Key) ResolveParent(JsonObject root, string path)
    {
        if (path[0] != '/')
        {
            throw new JsonPatchException($"'{path}' is not a JSON Pointer; it must start with '/'.");
        }

        var segments = path[1..].Split('/').Select(Unescape).ToArray();
        JsonNode current = root;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            current = current switch
            {
                JsonObject obj when obj.TryGetPropertyValue(segments[i], out var child) && child is not null => child,
                JsonArray array => array[ParseIndex(segments[i], array.Count, allowEnd: false)],
                _ => throw new JsonPatchException($"'{path}' does not exist."),
            };
        }

        return (current, segments[^1]);
    }

    private static int ParseIndex(string segment, int count, bool allowEnd)
    {
        if (!int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
            index > count ||
            (!allowEnd && index == count))
        {
            throw new JsonPatchException($"'{segment}' is not a valid array index.");
        }

        return index;
    }

    private static string Unescape(string segment)
    {
        return segment
            .Replace("~1", "/", StringComparison.Ordinal)
            .Replace("~0", "~", StringComparison.Ordinal);
    }
}
