namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// Materializes a stored <see cref="DocumentationSourceEntry"/> into a runtime
/// <see cref="IDocumentationSource"/> for a specific search strategy. Register an implementation to add
/// support for a new strategy that can be stored in the documentation source catalog.
/// </summary>
public interface IDocumentationSourceFactory
{
    /// <summary>
    /// Gets the strategy identifier this factory handles. This is matched against
    /// <see cref="DocumentationSourceEntry.Source"/> (see <see cref="DocumentationSourceStrategies"/>).
    /// </summary>
    string Strategy { get; }

    /// <summary>
    /// Creates a documentation source from the supplied entry.
    /// </summary>
    /// <param name="entry">The stored source entry to materialize.</param>
    /// <returns>The runtime documentation source.</returns>
    IDocumentationSource Create(DocumentationSourceEntry entry);
}
