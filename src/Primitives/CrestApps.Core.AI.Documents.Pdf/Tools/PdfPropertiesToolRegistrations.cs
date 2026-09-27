using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Registers the tools that change a PDF's document-level properties and protection: metadata, bookmarks, attachments, layers, links, passwords and permissions, digital signatures and optimization.
/// </summary>
internal static class PdfPropertiesToolRegistrations
{
    /// <summary>
    /// Registers the tools of this group.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
    }
}
