namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// Represents a searchable documentation knowledge base. Implement this interface to expose a custom
/// documentation source (for example a search index, an API, or a local corpus) to the documentation
/// search tool. Built-in sources are provided for public sites that expose a <c>sitemap.xml</c>.
/// </summary>
public interface IDocumentationSource
{
    /// <summary>
    /// Gets the unique logical name of this source. Callers can use this value to scope a search to a
    /// single source.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Searches the source for documents relevant to the supplied request.
    /// </summary>
    /// <param name="request">The search request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The relevant documents ordered by descending relevance.</returns>
    Task<IReadOnlyList<DocumentationSearchResult>> SearchAsync(DocumentationSearchRequest request, CancellationToken cancellationToken);
}
