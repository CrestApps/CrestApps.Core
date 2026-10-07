using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Registers the tools that check PDFs: integrity, rendering and layout quality, accessibility and standards compliance.
/// </summary>
internal static class PdfQualityToolRegistrations
{
    /// <summary>
    /// Registers the tools of this group.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddPdfTool<ValidatePdfTool>(ValidatePdfTool.TheName, "Validate PDF", "Checks a PDF's integrity: structure, objects, content streams and fonts.");
        services.AddPdfTool<CheckPdfQualityTool>(CheckPdfQualityTool.TheName, "Check PDF quality", "Checks rendering, content overflow, fonts, links and layout, and compares a composed PDF with its definition.");
        services.AddPdfTool<CheckPdfAccessibilityTool>(CheckPdfAccessibilityTool.TheName, "Check PDF accessibility", "Checks tagging, language, title, alternative text, form fields, links and other accessibility requirements.");
        services.AddPdfTool<TagPdfAccessibilityTool>(TagPdfAccessibilityTool.TheName, "Tag PDF accessibility", "Sets the language, title, tab order, field tooltips, link descriptions and figure alternative text of a PDF.");
        services.AddPdfTool<ValidatePdfComplianceTool>(ValidatePdfComplianceTool.TheName, "Validate PDF compliance", "Checks a PDF against the main requirements of PDF/A and PDF/UA.");
    }
}
