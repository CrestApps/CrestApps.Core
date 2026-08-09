namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// Resolves the complete set of documentation sources available to the documentation search tool. The
/// default implementation aggregates sources defined in options (through the documentation search
/// builder), sources stored in a catalog (for example a YesSql or EntityCore store), and custom
/// <see cref="IDocumentationSource"/> implementations registered in code.
/// </summary>
public interface IDocumentationSourceProvider
{
    /// <summary>
    /// Gets all documentation sources that can be searched.
    /// </summary>
    /// <param name="services">
    /// The request service provider used to resolve scoped services such as the documentation source
    /// catalog and custom sources.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The available documentation sources.</returns>
    ValueTask<IReadOnlyList<IDocumentationSource>> GetSourcesAsync(IServiceProvider services, CancellationToken cancellationToken = default);
}
