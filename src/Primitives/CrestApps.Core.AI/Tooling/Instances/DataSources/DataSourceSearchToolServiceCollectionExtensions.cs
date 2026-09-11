using CrestApps.Core.Builders;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Tooling.Instances.DataSources;

/// <summary>
/// Convenience registration for the built-in <see cref="DataSourceSearchToolInstanceSource"/>, which turns
/// an existing AI data source into a model-callable vector search function.
/// </summary>
public static class DataSourceSearchToolServiceCollectionExtensions
{
    /// <summary>
    /// Registers the built-in data source search source on the tool instances builder so users can create
    /// configured instances that search one of the existing AI data sources.
    /// </summary>
    /// <param name="builder">The tool instances builder.</param>
    /// <param name="configure">
    /// An optional delegate used to override the source display metadata (display name, description,
    /// category). Sensible defaults are applied when not overridden.
    /// </param>
    /// <returns>The tool instances builder, for chaining.</returns>
    public static CrestAppsAIToolInstancesBuilder AddDataSourceSearchSource(
        this CrestAppsAIToolInstancesBuilder builder,
        Action<AIToolInstanceSourceEntry> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddSource<DataSourceSearchToolInstanceSource>(DataSourceSearchToolConstants.SourceName, entry =>
        {
            entry.DisplayName = new LocalizedString(DataSourceSearchToolConstants.SourceName, "Data source search (vector)");
            entry.Description = new LocalizedString(
                DataSourceSearchToolConstants.SourceName,
                "Searches one of the existing AI data sources using semantic vector search against its knowledge base index.");
            entry.Category = new LocalizedString(DataSourceSearchToolConstants.Category, DataSourceSearchToolConstants.Category);

            configure?.Invoke(entry);
        });

        return builder;
    }
}
