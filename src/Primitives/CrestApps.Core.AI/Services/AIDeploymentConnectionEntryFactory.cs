using System.Text.Json.Nodes;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Support;
using Microsoft.AspNetCore.DataProtection;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Provides functionality for AI Deployment Connection Entry Factory.
/// </summary>
public static class AIDeploymentConnectionEntryFactory
{
    private const string ConnectionProtectorName = "AIProviderConnection";

    /// <summary>
    /// Creates the operation.
    /// </summary>
    /// <param name="deployment">The deployment.</param>
    /// <param name="dataProtectionProvider">The data protection provider.</param>
    public static AIProviderConnectionEntry Create(AIDeployment deployment, IDataProtectionProvider dataProtectionProvider)
    {
        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        if (deployment.Properties != null)
        {
            foreach (var property in deployment.Properties)
            {
                values[property.Key] = property.Value is JsonNode jsonNode
                ? jsonNode.GetRawValue()
                : property.Value;
            }
        }

        UnprotectApiKeys(values, dataProtectionProvider);
        AIProviderConnectionDeploymentNameNormalizer.Normalize(values);

return new AIProviderConnectionEntry(values);
    }

    private static void UnprotectApiKeys(IDictionary<string, object> values, IDataProtectionProvider dataProtectionProvider)
    {
        foreach (var (key, value) in values.ToList())
        {
            switch (value)
            {
                case IDictionary<string, object> nestedDictionary:
                    UnprotectApiKeys(nestedDictionary, dataProtectionProvider);
                    break;

                case List<object> items:
                    UnprotectApiKeys(items, dataProtectionProvider);
                    break;

                case string encryptedKey when
                string.Equals(key, "ApiKey", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(encryptedKey):
                    {
                        var protector = dataProtectionProvider.CreateProtector(ConnectionProtectorName);
                        values[key] = protector.Unprotect(encryptedKey);
                        break;
                    }
            }
        }
    }

    private static void UnprotectApiKeys(List<object> values, IDataProtectionProvider dataProtectionProvider)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case IDictionary<string, object> nestedDictionary:
                    UnprotectApiKeys(nestedDictionary, dataProtectionProvider);
                    break;

                case List<object> nestedList:
                    UnprotectApiKeys(nestedList, dataProtectionProvider);
                    break;
            }
        }
    }
}
