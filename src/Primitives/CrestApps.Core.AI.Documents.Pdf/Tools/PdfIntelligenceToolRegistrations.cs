using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Registers the tools that use a model to understand PDFs: OCR, image analysis, summaries, questions, entities, structured data, classification and cross-referencing.
/// </summary>
internal static class PdfIntelligenceToolRegistrations
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
