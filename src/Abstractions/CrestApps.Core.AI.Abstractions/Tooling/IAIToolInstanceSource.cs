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
/// Sources are registered with <c>AddAIToolInstanceSource&lt;TSource&gt;(name, configure)</c>, which
/// records the source's display metadata (display name, description, category) in
/// <c>AIOptions.ToolInstanceSources</c> and registers the behavior as a keyed service. A source
/// typically ships a settings model that it persists in <see cref="AIToolInstance.Properties"/> (via
/// <c>.Put()</c>/<c>.TryGet()</c>) and reads back inside the produced tool. The classic example is a
/// generic "call any HTTP API" source where the user provides the endpoint, authentication, and
/// headers, while the model only supplies the remaining open arguments (if any).
/// </remarks>
public interface IAIToolInstanceSource
{
    /// <summary>
    /// Creates the concrete <see cref="AITool"/> that the AI model can invoke for the supplied
    /// configured instance. Implementations must apply the instance's user-provided settings and use the
    /// supplied <see cref="AIToolInstanceSourceContext.FunctionName"/> and
    /// <see cref="AIToolInstanceSourceContext.Description"/> so the instance surfaces distinctly.
    /// </summary>
    /// <param name="context">The context describing the instance and the function metadata to expose.</param>
    /// <returns>The tool to expose to the AI model, or <see langword="null"/> to skip this instance.</returns>
    AITool CreateTool(AIToolInstanceSourceContext context);
}
