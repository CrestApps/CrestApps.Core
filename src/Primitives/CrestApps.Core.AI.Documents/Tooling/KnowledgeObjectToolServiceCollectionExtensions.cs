using CrestApps.Core.AI.Tooling;
using CrestApps.Core.Builders;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Documents.Tooling;

/// <summary>
/// Convenience registration for the built-in <see cref="KnowledgeObjectToolInstanceSource"/>.
/// </summary>
public static class KnowledgeObjectToolServiceCollectionExtensions
{
    /// <summary>
    /// Registers the built-in knowledge object source on the tool instances builder, so a user can create an
    /// instance that reads objects out of one ingested data source.
    /// </summary>
    /// <param name="builder">The tool instances builder.</param>
    /// <param name="configure">
    /// An optional delegate used to override the source display metadata. Sensible defaults are applied when
    /// not overridden.
    /// </param>
    /// <returns>The tool instances builder, for chaining.</returns>
    public static CrestAppsAIToolInstancesBuilder AddKnowledgeObjectSource(
        this CrestAppsAIToolInstancesBuilder builder,
        Action<AIToolInstanceSourceEntry> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddSource<KnowledgeObjectToolInstanceSource>(KnowledgeObjectToolConstants.SourceName, entry =>
        {
            entry.DisplayName = new LocalizedString(KnowledgeObjectToolConstants.SourceName, "Knowledge object (get by id)");
            entry.Description = new LocalizedString(
                KnowledgeObjectToolConstants.SourceName,
                "Reads one article, figure, chart or table in full from an ingested data source, by the identifier a search reported.");
            entry.Category = new LocalizedString(KnowledgeObjectToolConstants.Category, KnowledgeObjectToolConstants.Category);

            configure?.Invoke(entry);
        });

        return builder;
    }
}
