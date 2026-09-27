using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Registers the tools that change what existing PDF pages show: content, annotations, redaction,
/// sensitive-data detection and sanitizing.
/// </summary>
internal static class PdfEditingToolRegistrations
{
    /// <summary>
    /// Registers the tools of this group.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddPdfTool<RedactPdfTool>(RedactPdfTool.TheName, "Redact PDF", "Permanently removes text, images and areas from pages.");
        services.AddPdfTool<EditPdfContentTool>(EditPdfContentTool.TheName, "Edit PDF Content", "Replaces, adds or removes text and images on existing pages.");
        services.AddPdfTool<FindPdfSensitiveDataTool>(FindPdfSensitiveDataTool.TheName, "Find PDF Sensitive Data", "Finds personal, financial and other sensitive data before redaction.");
    }
}
