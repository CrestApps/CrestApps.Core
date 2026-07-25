using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Carries the information required to materialize an <see cref="AITool"/> for a configured
/// <see cref="AIToolDefinition"/>. Passed to <see cref="AIToolSource.CreateTool"/>.
/// </summary>
public sealed class AIToolSourceContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AIToolSourceContext"/> class.
    /// </summary>
    /// <param name="definition">The configured tool definition.</param>
    /// <param name="functionName">The unique function name to expose to the AI model.</param>
    /// <param name="description">The description to expose to the AI model.</param>
    public AIToolSourceContext(AIToolDefinition definition, string functionName, string description)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrEmpty(functionName);

        Definition = definition;
        FunctionName = functionName;
        Description = description;
    }

    /// <summary>
    /// Gets the configured tool definition whose settings should be bound to the produced tool.
    /// </summary>
    public AIToolDefinition Definition { get; }

    /// <summary>
    /// Gets the unique function name to expose to the AI model. This is derived per definition so that
    /// multiple definitions of the same source surface as distinct callable functions.
    /// </summary>
    public string FunctionName { get; }

    /// <summary>
    /// Gets the description to expose to the AI model, taken from the definition so the model can
    /// distinguish between definitions of the same source.
    /// </summary>
    public string Description { get; }
}
