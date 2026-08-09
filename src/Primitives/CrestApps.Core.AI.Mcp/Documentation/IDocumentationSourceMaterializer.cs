namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// Caches the runtime <see cref="IDocumentationSource"/> materialized for each configured documentation
/// search tool instance so an expensive crawled corpus or downloaded index is reused across searches.
/// A cached source is rebuilt only when the instance that defines it changes.
/// </summary>
public interface IDocumentationSourceMaterializer
{
    /// <summary>
    /// Gets the cached documentation source for the supplied key, creating it with <paramref name="factory"/>
    /// when it is missing or when the cached <paramref name="signature"/> no longer matches.
    /// </summary>
    /// <param name="key">A stable key that identifies the defining instance (for example its item id).</param>
    /// <param name="signature">A value that changes whenever the instance's settings change.</param>
    /// <param name="factory">The factory used to build the source when it must be created or rebuilt.</param>
    /// <returns>The cached or newly created documentation source.</returns>
    IDocumentationSource GetOrCreate(string key, string signature, Func<IDocumentationSource> factory);
}
