namespace CrestApps.Core;

/// <summary>
/// Marks a model as being associated with a named origin or provider,
/// enabling filtering and grouping by source (e.g., "OpenAI", "AzureOpenAI").
/// </summary>
public interface ISourceAwareModel
{
    /// <summary>
    /// Gets or sets the name of the source or provider that owns this model.
    /// </summary>
    /// <remarks>
    /// The setter is retained because framework code assigns this property through
    /// the interface type constraint (e.g., in <c>SourceCatalogManager</c>).
    /// </remarks>
    string Source { get; set; }
}
