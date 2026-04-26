using System.Text.Json;

namespace CrestApps.Core.Infrastructure;

/// <summary>
/// Provides extension methods for dictionary.
/// </summary>
public static class DictionaryExtensions
{
    public static string GetApiKey(this IDictionary<string, object> entry, bool throwException = true)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.GetStringValue("ApiKey", throwException);
    }

    public static Uri GetEndpoint(this IDictionary<string, object> entry, bool throwException = true)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var endpoint = entry.GetStringValue("Endpoint", throwException);
        Uri uri = null;
        if (throwException)
        {
            uri = new Uri(endpoint);
        }
        else if (!string.IsNullOrEmpty(endpoint))
        {
            Uri.TryCreate(endpoint, UriKind.Absolute, out uri);
        }

        return uri;
    }

    public static string GetStringValue(this IDictionary<string, object> entry, string key, bool throwException = false)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(key);

        if (entry.TryGetValue(key, out var value))
        {
            string stringValue;
            if (value is JsonElement jsonElement)
            {
                stringValue = jsonElement.GetString();
            }
            else if (value is string)
            {
                stringValue = value as string;
            }
            else
            {
                stringValue = value?.ToString();
            }

            if (throwException && string.IsNullOrWhiteSpace(stringValue))
            {
                throw new InvalidOperationException($"The '{key}' does not have a value in the dictionary.");
            }

            return stringValue;
        }

        if (!throwException)
        {
            return null;
        }

        throw new InvalidOperationException($"The '{key}' does not exist in the dictionary.");
    }

    public static bool GetBooleanOrFalseValue(this IDictionary<string, object> entry, string key, bool throwException = false)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(key);

        if (entry.TryGetValue(key, out var value))
        {
            if (value is bool booleanValue)
            {
                return booleanValue;
            }

            if (!throwException)
            {
                return false;
            }

            throw new InvalidOperationException($"The value for key '{key}' is not a valid boolean. Received '{value}', but expected true or false.");
        }

        if (!throwException)
        {
            return false;
        }

        throw new InvalidOperationException($"The '{key}' does not exist in the dictionary.");
    }
}
