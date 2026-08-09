namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// Represents a single documentation search query issued against an <see cref="IDocumentationSource"/>.
/// </summary>
public sealed class DocumentationSearchRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentationSearchRequest"/> class.
    /// </summary>
    /// <param name="query">The free-text search query.</param>
    public DocumentationSearchRequest(string query)
    {
        Query = query;
    }

    /// <summary>
    /// Gets the free-text search query.
    /// </summary>
    public string Query { get; }

    /// <summary>
    /// Gets or sets the maximum number of results the source should return.
    /// </summary>
    public int MaxResults { get; set; } = 5;
}
