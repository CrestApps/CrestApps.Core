using System.Text.Json;
using CrestApps.Core.Azure.Models;

namespace CrestApps.Core.Azure;

/// <summary>
/// Provides extension methods for dictionary.
/// </summary>
public static class DictionaryExtensions
{
    /// <summary>
    /// Gets azure authentication type.
    /// </summary>
    /// <param name="entry">The entry.</param>
    public static AzureAuthenticationType GetAzureAuthenticationType(this IDictionary<string, object> entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var authenticationTypeString = entry.GetStringValue("AuthenticationType");

        if (string.IsNullOrEmpty(authenticationTypeString) ||
            !Enum.TryParse<AzureAuthenticationType>(authenticationTypeString, true, out var authenticationType))
        {
            authenticationType = AzureAuthenticationType.Default;
        }

        return authenticationType;
    }

    /// <summary>
    /// Gets identity id.
    /// </summary>
    /// <param name="entry">The entry.</param>
    public static string GetIdentityId(this IDictionary<string, object> entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.GetStringValue("IdentityId", false);
    }

    /// <summary>
    /// Gets string value.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="key">The key.</param>
    /// <param name="throwException">The throw exception.</param>
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

        throw new InvalidOperationException($"The '{key}' does not exists in the dictionary.");
    }
}
