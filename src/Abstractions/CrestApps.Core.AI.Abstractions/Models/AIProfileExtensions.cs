using System.Text.Json;
using System.Text.Json.Nodes;

namespace CrestApps.Core.AI.Models;

/// <summary>
/// Extension methods for managing settings in AIProfile.
/// </summary>
public static class AIProfileExtensions
{
    private static JsonSerializerOptions _jsonOptions => ExtensibleEntityExtensions.JsonSerializerOptions;

    /// <summary>
    /// Retrieves settings of type <typeparamref name="T"/> from the profile.
    /// If the settings do not exist, a new instance of <typeparamref name="T"/> is returned.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="jsonSerializerOptions">The JSON serializer options.</param>
    public static T GetSettings<T>(this AIProfile profile, JsonSerializerOptions jsonSerializerOptions = null)
        where T : new()
        => profile.GetOrCreateSettings<T>(jsonSerializerOptions);

    /// <summary>
    /// Retrieves settings of type <typeparamref name="T"/> from the profile.
    /// If the settings do not exist, a new instance of <typeparamref name="T"/> is returned.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="jsonSerializerOptions">The JSON serializer options.</param>
    public static T GetOrCreateSettings<T>(this AIProfile profile, JsonSerializerOptions jsonSerializerOptions = null)
        where T : new()
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.Settings == null)
        {
            return new T();
        }

        return profile.Settings.TryGetValue(typeof(T).Name, out var value)
            ? DeserializeValue<T>(value, jsonSerializerOptions ?? _jsonOptions) ?? new T()
            : new T();
    }

    /// <summary>
    /// Attempts to retrieve settings of type <typeparamref name="T"/> from the profile.
    /// </summary>
    public static bool TryGetSettings<T>(this AIProfile profile, out T settings, JsonSerializerOptions jsonSerializerOptions = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.Settings == null)
        {
            settings = null;

            return false;
        }

        if (!profile.Settings.TryGetValue(typeof(T).Name, out var value) || value == null)
        {
            settings = null;

            return false;
        }

        settings = DeserializeValue<T>(value, jsonSerializerOptions ?? _jsonOptions);

        return settings != null;
    }

    /// <summary>
    /// Alters existing settings or adds new settings of type <typeparamref name="T"/> if one does not exists.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="setting">The setting.</param>
    /// <param name="jsonSerializerOptions">The JSON serializer options.</param>
    public static AIProfile AlterSettings<T>(this AIProfile profile, Action<T> setting, JsonSerializerOptions jsonSerializerOptions = null)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(setting);

        var settingsToMerge = profile.GetOrCreateSettings<T>(jsonSerializerOptions ?? _jsonOptions);

        setting(settingsToMerge);

        profile.Settings[typeof(T).Name] = JsonExtensions.FromObject(settingsToMerge, jsonSerializerOptions ?? _jsonOptions);

        return profile;
    }

    /// <summary>
    /// Sets or replaces the settings of type <typeparamref name="T"/> in the profile.
    /// </summary>
    public static AIProfile WithSettings<T>(this AIProfile profile, T settings, JsonSerializerOptions jsonSerializerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(settings);

        var jObject = JsonExtensions.FromObject(settings, jsonSerializerOptions ?? _jsonOptions);

        profile.Settings[typeof(T).Name] = jObject;

        return profile;
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
