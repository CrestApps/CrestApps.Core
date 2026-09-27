using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Registers the tools that build, format, preview, export and convert PDFs, edit their pages and work with
/// their forms.
/// </summary>
internal static class PdfAuthoringToolRegistrations
{
    /// <summary>
    /// Registers the tools of this group.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddPdfTool<GetPdfInfoTool>(GetPdfInfoTool.TheName, "Get PDF Info", "Lists the PDFs in the conversation, or describes one: pages, metadata, bookmarks, forms, attachments, layers and security.");
        services.AddPdfTool<CreatePdfTool>(CreatePdfTool.TheName, "Create PDF", "Starts a new composed PDF with its page setup, theme and first content.");
        services.AddPdfTool<AddPdfContentTool>(AddPdfContentTool.TheName, "Add PDF Content", "Adds, inserts, replaces or removes content blocks in a composed PDF.");
        services.AddPdfTool<FormatPdfTool>(FormatPdfTool.TheName, "Format PDF", "Records how a composed PDF looks: theme, page layout, running heads, page numbers, cover, contents and watermark.");
        services.AddPdfTool<PreviewPdfTool>(PreviewPdfTool.TheName, "Preview PDF", "Shows PDF pages as pictures in the conversation.");
        services.AddPdfTool<ExportPdfTool>(ExportPdfTool.TheName, "Export PDF", "Writes a PDF as a download.");
        services.AddPdfTool<EditPdfPagesTool>(EditPdfPagesTool.TheName, "Edit PDF Pages", "Merges, splits, extracts, reorders, rotates, deletes, inserts, crops and resizes pages, and stamps watermarks, page numbers and running text.");
    }
}
