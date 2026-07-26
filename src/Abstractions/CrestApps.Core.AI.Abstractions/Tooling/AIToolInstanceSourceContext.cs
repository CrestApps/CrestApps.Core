using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Carries the information required to materialize an <see cref="AITool"/> for a configured
/// <see cref="AIToolInstance"/>. Passed to <see cref="IAIToolInstanceSource.CreateTool"/>.
/// </summary>
public sealed class AIToolInstanceSourceContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIToolInstanceSourceContext"/> class.
    /// </summary>
    /// <param name="instance">The configured tool instance.</param>
    /// <param name="functionName">The unique function name to expose to the AI model.</param>
    /// <param name="description">The description to expose to the AI model.</param>
    public AIToolInstanceSourceContext(AIToolInstance instance, string functionName, string description)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentException.ThrowIfNullOrEmpty(functionName);

        Instance = instance;
        FunctionName = functionName;
        Description = description;
    }

    /// <summary>
    /// Gets the configured tool instance whose settings should be bound to the produced tool.
    /// </summary>
    public AIToolInstance Instance { get; }

    /// <summary>
    /// Gets the unique function name to expose to the AI model. This is derived per instance so that
    /// multiple instances of the same source surface as distinct callable functions.
    /// </summary>
    public string FunctionName { get; }

    /// <summary>
    /// Gets the description to expose to the AI model, taken from the instance so the model can
    /// distinguish between instances of the same source.
    /// </summary>
    public string Description { get; }
}
