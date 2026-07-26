using Microsoft.Extensions.AI;

namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// A developer-authored, parameterized tool blueprint that end users configure one or more times as
/// <see cref="AIToolInstance"/> catalog entries. A source is registered under a unique name (stored as
/// the <see cref="CrestApps.Core.Models.SourceCatalogEntry.Source"/> of every <see cref="AIToolInstance"/>
/// created from it) and is responsible for turning a configured instance into a concrete
/// <see cref="AITool"/> whose behavior is bound to the user's settings.
/// </summary>
/// <remarks>
/// Sources are registered with <c>AddSource&lt;TSource&gt;(name, configure)</c> on the tool instances
/// builder, which records the source's display metadata (display name, description, category) in
/// <c>AIOptions.ToolInstanceSources</c> and registers the behavior as a keyed service. The registration
/// key is the source name, so there is no need for the source to carry its own name — the same key is
/// used to resolve the source for an instance via its <see cref="AIToolInstance.Source"/> value. A source
/// typically ships a settings model that it persists in <see cref="AIToolInstance.Properties"/> (via
/// <c>.Put()</c>/<c>.TryGet()</c>) and reads back inside the produced tool. The classic example is a
/// generic "call any HTTP API" source where the user provides the endpoint, authentication, and headers,
/// while the model only supplies the remaining open arguments (if any).
/// </remarks>
public interface IAIToolInstanceSource
{
    /// <summary>
    /// Creates the concrete <see cref="AITool"/> that the AI model can invoke for the supplied configured
    /// instance. Implementations must apply the instance's user-provided settings and derive the
    /// model-facing function name and description from the instance (via
    /// <see cref="AIToolInstanceExtensions.GetFunctionName"/> and <see cref="AIToolInstance.Description"/>)
    /// so multiple instances of the same source surface as distinct callable functions.
    /// </summary>
    /// <param name="instance">The configured tool instance whose settings should be bound to the produced tool.</param>
    /// <returns>The tool to expose to the AI model, or <see langword="null"/> to skip this instance.</returns>
    AITool CreateTool(AIToolInstance instance);
}
