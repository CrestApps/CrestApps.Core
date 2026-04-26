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
    {
        if (profile.Settings == null)
        {
            return new T();
        }

        var node = profile.Settings[typeof(T).Name];

        if (node == null)
        {
            return new T();
        }

        return node.Deserialize<T>(jsonSerializerOptions ?? _jsonOptions) ?? new T();
    }

    /// <summary>
    /// Attempts to retrieve settings of type <typeparamref name="T"/> from the profile.
    /// </summary>
    public static bool TryGetSettings<T>(this AIProfile profile, out T settings, JsonSerializerOptions jsonSerializerOptions = null)
        where T : class
    {
        if (profile.Settings == null)
        {
            settings = null;

            return false;
        }

        var node = profile.Settings[typeof(T).Name];

        if (node == null)
        {
            settings = null;

            return false;
        }

        settings = node.Deserialize<T>(jsonSerializerOptions ?? _jsonOptions);

        return true;
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
        var existingJObject = profile.Settings[typeof(T).Name] as JsonObject;

        if (existingJObject == null)
        {
            existingJObject = JsonExtensions.FromObject(new T(), jsonSerializerOptions ?? _jsonOptions);

            profile.Settings[typeof(T).Name] = existingJObject;
        }

        var settingsToMerge = existingJObject.Deserialize<T>(jsonSerializerOptions ?? _jsonOptions);

        setting(settingsToMerge);

        profile.Settings[typeof(T).Name] = JsonExtensions.FromObject(settingsToMerge, jsonSerializerOptions ?? _jsonOptions);

        return profile;
    }

    /// <summary>
    /// Sets or replaces the settings of type <typeparamref name="T"/> in the profile.
    /// </summary>
    public static AIProfile WithSettings<T>(this AIProfile profile, T settings, JsonSerializerOptions jsonSerializerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var jObject = JsonExtensions.FromObject(settings, jsonSerializerOptions ?? _jsonOptions);

        profile.Settings[typeof(T).Name] = jObject;

        return profile;
    }
}
