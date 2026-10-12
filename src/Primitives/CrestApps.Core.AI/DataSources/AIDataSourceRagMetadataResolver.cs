using CrestApps.Core.AI.Models;

namespace CrestApps.Core.AI.DataSources;

/// <summary>
/// Reads the retrieval settings (filter, strictness, top documents, scope and object types) that a resource
/// stores for its data source.
/// </summary>
/// <remarks>
/// An AI profile and a chat interaction both store these settings as <see cref="AIDataSourceRagMetadata"/>.
/// Every retrieval path reads them through this one method, so the preemptive search and the search tool that
/// the model calls apply the same settings to the same resource.
/// </remarks>
internal static class AIDataSourceRagMetadataResolver
{
    /// <summary>
    /// Gets the data source retrieval settings of the resource.
    /// </summary>
    /// <param name="resource">The resource of the completion, usually an <see cref="AIProfile"/> or a <see cref="ChatInteraction"/>.</param>
    /// <returns>The retrieval settings, or <see langword="null"/> when the resource stores none.</returns>
    public static AIDataSourceRagMetadata Resolve(object resource)
    {
        if (resource is AIProfile profile &&
            profile.TryGet<AIDataSourceRagMetadata>(out var profileRagMetadata))
        {
            return profileRagMetadata;
        }

        if (resource is ChatInteraction interaction &&
            interaction.TryGet<AIDataSourceRagMetadata>(out var interactionRagMetadata))
        {
            return interactionRagMetadata;
        }

        return null;
    }
}
