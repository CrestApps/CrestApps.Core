using CrestApps.Core.Infrastructure;

namespace CrestApps.Core.AI.Services;

internal static class AIProviderConnectionDeploymentNameNormalizer
{
    /// <summary>
    /// Normalizes the operation.
    /// </summary>
    /// <param name="values">The values.</param>
    public static void Normalize(IDictionary<string, object> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        Normalize(values, values, "ChatDeploymentName", "ChatDeploymentName", "DeploymentName", "DefaultChatDeploymentName", "DefaultDeploymentName");
        Normalize(values, values, "UtilityDeploymentName", "UtilityDeploymentName", "DefaultUtilityDeploymentName");
        Normalize(values, values, "EmbeddingDeploymentName", "EmbeddingDeploymentName", "DefaultEmbeddingDeploymentName");
        Normalize(values, values, "ImagesDeploymentName", "ImagesDeploymentName", "DefaultImagesDeploymentName");
        Normalize(values, values, "SpeechToTextDeploymentName", "SpeechToTextDeploymentName", "DefaultSpeechToTextDeploymentName");
    }

    /// <summary>
    /// Copys normalized.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="destination">The destination.</param>
    public static void CopyNormalized(IDictionary<string, object> source, IDictionary<string, object> destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        Normalize(source, destination, "ChatDeploymentName", "ChatDeploymentName", "DeploymentName", "DefaultChatDeploymentName", "DefaultDeploymentName");
        Normalize(source, destination, "UtilityDeploymentName", "UtilityDeploymentName", "DefaultUtilityDeploymentName");
        Normalize(source, destination, "EmbeddingDeploymentName", "EmbeddingDeploymentName", "DefaultEmbeddingDeploymentName");
        Normalize(source, destination, "ImagesDeploymentName", "ImagesDeploymentName", "DefaultImagesDeploymentName");
        Normalize(source, destination, "SpeechToTextDeploymentName", "SpeechToTextDeploymentName", "DefaultSpeechToTextDeploymentName");
    }

    private static void Normalize(IDictionary<string, object> source, IDictionary<string, object> destination, string targetKey, params string[] sourceKeys)
    {
        var value = GetStringValue(source, sourceKeys);

        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        destination[targetKey] = value;
    }

    private static string GetStringValue(IDictionary<string, object> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = values.GetStringValue(key, false);

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
