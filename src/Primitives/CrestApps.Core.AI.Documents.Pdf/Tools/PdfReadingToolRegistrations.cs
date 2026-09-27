using Microsoft.Extensions.DependencyInjection;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Registers the tools that read PDFs: text, tables, images, structure, links, search, page content, layout, language, outline, comparison and conversion out of PDF.
/// </summary>
internal static class PdfReadingToolRegistrations
{
    /// <summary>
    /// Registers the tools of this group.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddPdfTool<ExtractPdfTextTool>(ExtractPdfTextTool.TheName, "Extract PDF text", "Extracts a PDF's text page by page in reading order, optionally as lines or words with positions and fonts.");
        services.AddPdfTool<ExtractPdfTablesTool>(ExtractPdfTablesTool.TheName, "Extract PDF tables", "Detects the tables in a PDF, joins tables that run across pages, and returns or exports their cells.");
        services.AddPdfTool<ExtractPdfImagesTool>(ExtractPdfImagesTool.TheName, "Extract PDF images", "Lists the pictures embedded in a PDF, and shows, keeps or packs them for download.");
        services.AddPdfTool<ExtractPdfStructureTool>(ExtractPdfStructureTool.TheName, "Extract PDF structure", "Returns a PDF's headings, paragraphs, lists, tables and figures in reading order.");
        services.AddPdfTool<ExtractPdfLinksTool>(ExtractPdfLinksTool.TheName, "Extract PDF links", "Lists a PDF's web and internal links and named destinations, and checks them without following any.");
        services.AddPdfTool<SearchPdfTool>(SearchPdfTool.TheName, "Search PDF", "Finds text or patterns in a PDF and returns matches with pages, snippets and positions.");
        services.AddPdfTool<GetPdfPageContentTool>(GetPdfPageContentTool.TheName, "Get PDF page content", "Describes one page: text blocks, fonts, pictures, links, annotations and form fields.");
        services.AddPdfTool<AnalyzePdfLayoutTool>(AnalyzePdfLayoutTool.TheName, "Analyze PDF layout", "Describes page layout: columns, blocks in reading order with their roles, figures and tables.");
        services.AddPdfTool<DetectPdfLanguageTool>(DetectPdfLanguageTool.TheName, "Detect PDF language", "Detects the language a PDF is written in, overall and by page, against the language it declares.");
        services.AddPdfTool<GeneratePdfOutlineTool>(GeneratePdfOutlineTool.TheName, "Generate PDF outline", "Builds a PDF's outline from its bookmarks, or from the headings its type sizes set apart.");
        services.AddPdfTool<ComparePdfsTool>(ComparePdfsTool.TheName, "Compare PDFs", "Compares PDFs with a baseline and reports text added, removed and changed, with pages.");
        services.AddPdfTool<ConvertFromPdfTool>(ConvertFromPdfTool.TheName, "Convert from PDF", "Converts a PDF to Word, Excel, Markdown, text, HTML, JSON, CSV or SVG page pictures.");
    }
}
