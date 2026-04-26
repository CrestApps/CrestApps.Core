namespace CrestApps.Core.AI.Services;

/// <summary>
/// Provides functionality for AI Provider Name Normalizer.
/// </summary>
public static class AIProviderNameNormalizer
{
    private const string _azureOpenAIClientName = "Azure";

    /// <summary>
    /// Normalizes the operation.
    /// </summary>
    /// <param name="providerName">The provider name.</param>
    public static string Normalize(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return providerName;
        }

        return string.Equals(providerName, "AzureOpenAI", StringComparison.OrdinalIgnoreCase)
            ? _azureOpenAIClientName
            : providerName;
    }
}
