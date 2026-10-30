using CrestApps.Core.AI.Models;
using CrestApps.Core.Infrastructure.Indexing.Models;

namespace CrestApps.Core.AI.Memory;

internal static class AIMemoryIndexProfileExtensions
{
    /// <summary>
    /// Gets the embedding deployment an AI Memory index profile uses. The deployment saved on the profile's
    /// <see cref="DataSourceIndexProfileMetadata"/> wins over the profile's own property, and indexing and search
    /// must both resolve it here so they embed with the same model.
    /// </summary>
    /// <param name="indexProfile">The AI Memory index profile.</param>
    public static string GetMemoryEmbeddingDeploymentName(this SearchIndexProfile indexProfile)
    {
        if (indexProfile.TryGet(out DataSourceIndexProfileMetadata metadata) &&
            !string.IsNullOrWhiteSpace(metadata.EmbeddingDeploymentName))
        {
            return metadata.EmbeddingDeploymentName;
        }

        return indexProfile.EmbeddingDeploymentName;
    }
}
