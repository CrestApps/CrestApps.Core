using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Tooling;
using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Documents.Tooling;

/// <summary>
/// The built-in <see cref="IAIToolInstanceSource"/> that lets a model read one knowledge object in full by
/// the identifier a search reported.
/// </summary>
public sealed class KnowledgeObjectToolInstanceSource : IAIToolInstanceSource
{
    /// <summary>
    /// Creates the configured tool.
    /// </summary>
    /// <param name="instance">The configured tool instance.</param>
    /// <returns>The tool.</returns>
    public AITool CreateTool(AIToolInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var settings = instance.GetOrCreate<KnowledgeObjectToolSettings>();

        var functionName = instance.GetFunctionName();
        var description = string.IsNullOrWhiteSpace(instance.Description)
            ? "Reads one knowledge object - an article, a figure, a chart or a table - in full, by the identifier a previous search reported."
            : instance.Description;

        return new KnowledgeObjectToolFunction(functionName, description, settings);
    }
}
