using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Documents.Tooling;

/// <summary>
/// The built-in <see cref="IAIToolInstanceSource"/> that lets a model enumerate what one ingested data source
/// holds, rather than only search it.
/// </summary>
public sealed class KnowledgeObjectListToolInstanceSource : IAIToolInstanceSource
{
    /// <summary>
    /// Creates the configured tool.
    /// </summary>
    /// <param name="instance">The configured tool instance.</param>
    /// <returns>The tool.</returns>
    public AITool CreateTool(AIToolInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var settings = instance.GetOrCreate<KnowledgeObjectListToolSettings>();

        var functionName = instance.GetFunctionName();
        var description = string.IsNullOrWhiteSpace(instance.Description)
            ? "Lists what an ingested data source holds - its figures, charts, tables or articles - optionally inside one article, and walks from one object to its parent or to the objects beside it. Use this for questions about the set, such as which figures or tables exist; searching ranks against a query and cannot answer them."
            : instance.Description;

        return new KnowledgeObjectListToolFunction(functionName, description, settings);
    }
}
