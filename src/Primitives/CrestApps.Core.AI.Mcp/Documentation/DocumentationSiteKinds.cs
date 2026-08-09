namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// Well-known documentation generator identifiers used for the <see cref="DocumentationSite.Kind"/>
/// hint. The value is an open-ended string so hosts can define their own kinds for custom generators;
/// the built-in crawler treats it as a hint and consumes any site that exposes a standard
/// <c>sitemap.xml</c> regardless of the selected kind.
/// </summary>
public static class DocumentationSiteKinds
{
    /// <summary>
    /// A Docusaurus documentation site.
    /// </summary>
    public const string Docusaurus = "docusaurus";

    /// <summary>
    /// A MkDocs documentation site.
    /// </summary>
    public const string MkDocs = "mkdocs";
}
