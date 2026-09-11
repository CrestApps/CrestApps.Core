using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Tooling.Instances.DataSources;

/// <summary>
/// The built-in <see cref="IAIToolInstanceSource"/> that lets users expose one existing AI data source to
/// the model as a callable vector search function. Each configured <see cref="AIToolInstance"/> binds one
/// data source together with its retrieval parameters; the AI model only supplies the search phrase.
/// </summary>
public sealed class DataSourceSearchToolInstanceSource : IAIToolInstanceSource
{
    /// <summary>
    /// Creates the <see cref="DataSourceSearchToolFunction"/> bound to the supplied instance's settings.
    /// </summary>
    /// <param name="instance">The configured tool instance whose settings should be bound to the produced tool.</param>
    /// <returns>The configured data source search function.</returns>
    public AITool CreateTool(AIToolInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var settings = instance.GetOrCreate<DataSourceSearchToolSettings>();

        var functionName = instance.GetFunctionName();
        var description = string.IsNullOrWhiteSpace(instance.Description)
            ? "Searches the configured knowledge base using semantic vector search and returns the most relevant content with citations."
            : instance.Description;

        return new DataSourceSearchToolFunction(functionName, description, settings);
    }
}
