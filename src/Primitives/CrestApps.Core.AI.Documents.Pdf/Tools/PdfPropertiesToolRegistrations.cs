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

        services.AddPdfTool<EditPdfMetadataTool>(EditPdfMetadataTool.TheName, "Edit PDF Metadata", "Updates a PDF's title, author, subject, keywords, language and custom properties.");
        services.AddPdfTool<AddPdfBookmarksTool>(AddPdfBookmarksTool.TheName, "Add PDF Bookmarks", "Lists, adds, replaces, clears or generates a PDF's bookmarks from its headings.");
        services.AddPdfTool<ManagePdfAttachmentsTool>(ManagePdfAttachmentsTool.TheName, "Manage PDF Attachments", "Lists, adds, extracts and removes the files embedded in a PDF.");
        services.AddPdfTool<ManagePdfLayersTool>(ManagePdfLayersTool.TheName, "Manage PDF Layers", "Lists a PDF's layers and shows or hides them by default.");
        services.AddPdfTool<AddPdfLinksTool>(AddPdfLinksTool.TheName, "Add PDF Links", "Adds web links and links to pages over text or areas of a PDF.");
        services.AddPdfTool<ProtectPdfTool>(ProtectPdfTool.TheName, "Protect PDF", "Encrypts a PDF with an open password and permissions.");
        services.AddPdfTool<RemovePdfSecurityTool>(RemovePdfSecurityTool.TheName, "Remove PDF Security", "Removes a PDF's password protection when the user supplies its password.");
        services.AddPdfTool<SignPdfTool>(SignPdfTool.TheName, "Sign PDF", "Digitally signs a PDF with the signing certificate the host configured.");
        services.AddPdfTool<VerifyPdfSignatureTool>(VerifyPdfSignatureTool.TheName, "Verify PDF Signature", "Verifies a PDF's digital signatures and whether it changed after signing.");
        services.AddPdfTool<OptimizePdfTool>(OptimizePdfTool.TheName, "Optimize PDF", "Compresses and optimizes a PDF, reporting the size before and after.");
    }
}
