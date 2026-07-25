using Microsoft.Extensions.AI;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Tooling;

/// <summary>
/// A developer-authored, parameterized tool blueprint that end users configure one or more times as
/// <see cref="AIToolDefinition"/> catalog entries. A source is identified by a unique <see cref="Name"/>,
/// which is stored as the <see cref="CrestApps.Core.Models.SourceCatalogEntry.Source"/> of every
/// <see cref="AIToolDefinition"/> created from it, and is responsible for turning a configured
/// definition into a concrete <see cref="AITool"/> whose behavior is bound to the user's settings.
/// </summary>
/// <remarks>
/// Sources are registered with <c>AddAIToolSource&lt;TSource&gt;()</c> and surfaced as an
/// <see cref="IEnumerable{T}"/> of <see cref="AIToolSource"/>. A source typically ships a settings
/// model that it persists in <see cref="AIToolDefinition.Properties"/> (via <c>.Put()</c>/<c>.TryGet()</c>)
/// and reads back inside the produced tool. The classic example is a generic "call any HTTP API" source
/// where the user provides the endpoint, authentication, and headers, while the model only supplies the
/// remaining open arguments (if any). Because the metadata (<see cref="DisplayName"/>,
/// <see cref="Description"/>, <see cref="Category"/>) lives directly on the source, no separate options,
/// entry, or builder types are required.
/// </remarks>
public abstract class AIToolSource
{
    /// <summary>
    /// Gets the unique registered name of this source. This value is stored as the
    /// <see cref="CrestApps.Core.Models.SourceCatalogEntry.Source"/> of every
    /// <see cref="AIToolDefinition"/> created from the source.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Gets the friendly display name shown when choosing a source to configure a new definition.
    /// Defaults to the <see cref="Name"/>.
    /// </summary>
    public virtual LocalizedString DisplayName => new(Name, Name);

    /// <summary>
    /// Gets the description that explains what kinds of definitions this source produces. Defaults to
    /// the <see cref="Name"/>.
    /// </summary>
    public virtual LocalizedString Description => new(Name, Name);

    /// <summary>
    /// Gets an optional category used to group sources in the management UI. Defaults to
    /// <see langword="null"/>.
    /// </summary>
    public virtual string Category => null;

    /// <summary>
    /// Creates the concrete <see cref="AITool"/> that the AI model can invoke for the supplied
    /// configured definition. Implementations must apply the definition's user-provided settings and use
    /// the supplied <see cref="AIToolSourceContext.FunctionName"/> and
    /// <see cref="AIToolSourceContext.Description"/> so the definition surfaces distinctly.
    /// </summary>
    /// <param name="context">The context describing the definition and the function metadata to expose.</param>
    /// <returns>The tool to expose to the AI model, or <see langword="null"/> to skip this definition.</returns>
    public abstract AITool CreateTool(AIToolSourceContext context);
}
