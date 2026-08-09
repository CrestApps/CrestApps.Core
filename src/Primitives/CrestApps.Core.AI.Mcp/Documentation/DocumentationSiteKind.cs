namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// Identifies the documentation generator that produced a configured site. The value is a hint that
/// lets the built-in crawler adapt to well-known layouts; most static generators expose a standard
/// <c>sitemap.xml</c> that the crawler can consume regardless of the selected kind.
/// </summary>
public enum DocumentationSiteKind
{
    /// <summary>
    /// The generator is unknown and the crawler should use its generic sitemap-based strategy.
    /// </summary>
    Auto,

    /// <summary>
    /// A Docusaurus documentation site.
    /// </summary>
    Docusaurus,

    /// <summary>
    /// A MkDocs documentation site.
    /// </summary>
    MkDocs,
}
