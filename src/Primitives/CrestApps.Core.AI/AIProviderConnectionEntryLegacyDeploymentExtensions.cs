using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure;

namespace CrestApps.Core.AI;

public static class AIProviderConnectionEntryLegacyDeploymentExtensions
{
    public static string GetLegacyChatDeploymentName(this AIProviderConnectionEntry connection)
        => GetLegacyString(connection, "ChatDeploymentName", "DeploymentName", "DefaultChatDeploymentName", "DefaultDeploymentName");

    public static string GetLegacyUtilityDeploymentName(this AIProviderConnectionEntry connection)
        => GetLegacyString(connection, "UtilityDeploymentName", "DefaultUtilityDeploymentName");

    public static string GetLegacyEmbeddingDeploymentName(this AIProviderConnectionEntry connection)
        => GetLegacyString(connection, "EmbeddingDeploymentName", "DefaultEmbeddingDeploymentName");

    public static string GetLegacyImageDeploymentName(this AIProviderConnectionEntry connection)
        => GetLegacyString(connection, "ImagesDeploymentName", "DefaultImagesDeploymentName");

    public static string GetLegacySpeechToTextDeploymentName(this AIProviderConnectionEntry connection)
        => GetLegacyString(connection, "SpeechToTextDeploymentName", "DefaultSpeechToTextDeploymentName");

    private static string GetLegacyString(AIProviderConnectionEntry connection, params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(connection);

        foreach (var key in keys)
        {
            var value = connection.GetStringValue(key, false);

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
