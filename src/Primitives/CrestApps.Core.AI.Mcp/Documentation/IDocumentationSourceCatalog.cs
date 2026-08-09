using CrestApps.Core.Services;

namespace CrestApps.Core.AI.Mcp.Documentation;

/// <summary>
/// A catalog of <see cref="DocumentationSourceEntry"/> items. Implementations aggregate documentation
/// source entries from all registered catalog sources (for example a YesSql or EntityCore store) so the
/// documentation search tool can materialize sources defined in a database or through a UI in addition
/// to those registered in code.
/// </summary>
public interface IDocumentationSourceCatalog : INamedSourceCatalog<DocumentationSourceEntry>
{
}
