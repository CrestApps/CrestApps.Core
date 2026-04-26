using System.Text.Json;
using System.Text.Json.Nodes;

namespace CrestApps.Core;

/// <summary>
/// Extension methods for <see cref="ExtensibleEntity"/> to provide dynamic property storage,
/// matching the patterns from OrchardCore.Entities.Entity.
/// </summary>
public static class ExtensibleEntityExtensions
{
    private static JsonSerializerOptions _jsonOptions = ExtensibleEntityJsonOptions.CreateDefaultSerializerOptions();

    /// <summary>
    /// Gets or sets the <see cref="JsonSerializerOptions"/> used for serializing and
    /// deserializing extensible entity properties.
    /// </summary>
    /// <remarks>
    /// This property is initialized with sensible defaults. To customize, either:
    /// <list type="bullet">
    /// <item>Set this property directly at application startup before any serialization occurs.</item>
    /// <item>Use the DI options pattern with <see cref="ExtensibleEntityJsonOptions"/> (requires CrestApps.Core).</item>
    /// </list>
    /// </remarks>
    public static JsonSerializerOptions JsonSerializerOptions
    {
        get => _jsonOptions;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _jsonOptions = value;
        }
    }

    /// <summary>
    /// Gets a strongly-typed object stored in the entity's properties.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <param name="jsonSerializerOptions">The JSON serializer options.</param>
    public static T GetOrCreate<T>(this ExtensibleEntity entity, JsonSerializerOptions jsonSerializerOptions = null)
        where T : new()
    {
        ArgumentNullException.ThrowIfNull(entity);

        var key = typeof(T).Name;

        return entity.Properties.TryGetValue(key, out var value)
            ? DeserializeValue<T>(value, jsonSerializerOptions ?? _jsonOptions) ?? new T()
            : new T();
    }

    /// <summary>
    /// Gets a strongly-typed object stored in the entity's properties, or null if not found.
    /// </summary>
    public static T Get<T>(this ExtensibleEntity entity, string name, JsonSerializerOptions jsonSerializerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentException.ThrowIfNullOrEmpty(name);

        return entity.Properties.TryGetValue(name, out var value)
            ? DeserializeValue<T>(value, jsonSerializerOptions ?? _jsonOptions)
            : default;
    }

    /// <summary>
    /// Stores a strongly-typed object in the entity's properties using the type name as key.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <param name="value">The value.</param>
    public static ExtensibleEntity Put<T>(this ExtensibleEntity entity, T value)
        where T : new()
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.Properties[typeof(T).Name] = value;

        return entity;
    }

    /// <summary>
    /// Stores a value in the entity's properties using a named key.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <param name="name">The name.</param>
    /// <param name="value">The value.</param>
    public static ExtensibleEntity Put(this ExtensibleEntity entity, string name, object value)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentException.ThrowIfNullOrEmpty(name);

        entity.Properties[name] = value;

        return entity;
    }

    /// <summary>
    /// Tries to get a strongly-typed object stored in the entity's properties.
    /// Returns <c>true</c> if a non-null value was found and deserialized.
    /// </summary>
    public static bool TryGet<T>(this ExtensibleEntity entity, out T result, JsonSerializerOptions jsonSerializerOptions = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(entity);

        var key = typeof(T).Name;

        if (entity.Properties.TryGetValue(key, out var value) && value is not null)
        {
            result = DeserializeValue<T>(value, jsonSerializerOptions ?? _jsonOptions);

            return result is not null;
        }

        result = default;

        return false;
    }

    /// <summary>
    /// Checks if the entity's properties contain a key with the given type name.
    /// </summary>
    public static bool Has<T>(this ExtensibleEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return entity.Properties.ContainsKey(typeof(T).Name);
    }

    /// <summary>
    /// Modifies a stored object in-place. If no object exists, a new instance is created,
    /// modified, and stored.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <param name="alter">The alter.</param>
    /// <param name="jsonSerializerOptions">The JSON serializer options.</param>
    public static ExtensibleEntity Alter<T>(this ExtensibleEntity entity, Action<T> alter, JsonSerializerOptions jsonSerializerOptions = null)
        where T : new()
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(alter);

        var obj = entity.GetOrCreate<T>(jsonSerializerOptions ?? _jsonOptions);

        alter(obj);

        entity.Put(obj);

        return entity;
    }

    /// <summary>
    /// Removes a stored object from the entity's properties using the type name as key.
    /// </summary>
    public static ExtensibleEntity Remove<T>(this ExtensibleEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.Properties.Remove(typeof(T).Name);

        return entity;
    }

    private static T DeserializeValue<T>(object value, JsonSerializerOptions jsonSerializerOptions = null)
    {
        if (value is null)
        {
            return default;
        }

        if (value is T typed)
        {
            return typed;
        }

        if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.Null)
            {
                return default;
            }

            return jsonElement.Deserialize<T>(jsonSerializerOptions ?? _jsonOptions);
        }

        if (value is JsonNode jsonNode)
        {
            return jsonNode.Deserialize<T>(jsonSerializerOptions ?? _jsonOptions);
        }

        var json = JsonSerializer.Serialize(value, jsonSerializerOptions ?? _jsonOptions);

        return JsonSerializer.Deserialize<T>(json, jsonSerializerOptions ?? _jsonOptions);
    }
}
