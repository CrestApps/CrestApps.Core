using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// Defines a developer-authored, parameterized tool template that end users can instantiate one or
/// more times with their own settings. Each registered definition is identified by a unique
/// <see cref="Name"/> and is responsible for turning a configured <see cref="AIToolInstance"/> into a
/// concrete <see cref="AITool"/> whose behavior is bound to the instance's settings.
/// </summary>
/// <remarks>
/// Definitions are registered as keyed services (keyed by <see cref="Name"/>) via
/// <c>AddAIToolInstanceDefinition</c>. A definition typically ships a settings model that it persists
/// in <see cref="AIToolInstance.Properties"/> and reads back inside the produced tool. The classic
/// example is a generic "call any HTTP API" definition where the user provides the endpoint,
/// authentication, and headers, while the model only supplies the remaining open arguments (if any).
/// </remarks>
public interface IAIToolInstanceDefinition
{
    /// <summary>
    /// Gets the unique registered name of this definition. This value is stored as the
    /// <see cref="AIToolInstance.Source"/> of every instance created from the definition.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Creates the concrete <see cref="AITool"/> that the AI model can invoke for the supplied
    /// configured instance. Implementations must apply the instance's user-provided settings and use
    /// the supplied <see cref="AIToolInstanceToolContext.FunctionName"/> and
    /// <see cref="AIToolInstanceToolContext.Description"/> so the instance surfaces distinctly.
    /// </summary>
    /// <param name="context">The context describing the instance and the function metadata to expose.</param>
    /// <returns>The tool to expose to the AI model, or <see langword="null"/> to skip this instance.</returns>
    AITool CreateTool(AIToolInstanceToolContext context);
}
