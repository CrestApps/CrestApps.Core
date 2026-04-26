namespace CrestApps.Core.Models;

/// <summary>
/// A catalog item that is associated with a named provider or source,
/// implementing <see cref="ISourceAwareModel"/> to expose the <see cref="Source"/> property.
/// </summary>
public class SourceCatalogEntry : CatalogItem, ISourceAwareModel
{
    /// <summary>
    /// Gets the name of the source for this profile.
    /// </summary>
    public string Source { get; set; }
}
