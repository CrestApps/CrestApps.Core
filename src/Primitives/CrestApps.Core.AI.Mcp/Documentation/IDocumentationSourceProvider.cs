namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// Resolves the complete set of documentation sources available to the documentation search tool.
/// The default implementation aggregates sources registered in code with the built-in crawler
/// sources materialized from <see cref="DocumentationSearchOptions.Sites"/>.
/// </summary>
public interface IDocumentationSourceProvider
{
    /// <summary>
    /// Gets all documentation sources that can be searched.
    /// </summary>
    /// <returns>The available documentation sources.</returns>
    IReadOnlyList<IDocumentationSource> GetSources();
}
