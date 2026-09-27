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

        services.AddPdfTool<OcrPdfTool>(OcrPdfTool.TheName, "OCR PDF", "Recognizes the text of scanned PDF pages with a vision model, optionally adding a searchable text layer.");
        services.AddPdfTool<AnalyzePdfImagesTool>(AnalyzePdfImagesTool.TheName, "Analyze PDF Images", "Describes the images, charts and diagrams embedded in a PDF with a vision model.");
        services.AddPdfTool<SummarizePdfTool>(SummarizePdfTool.TheName, "Summarize PDF", "Summarizes a PDF or selected pages with page references.");
        services.AddPdfTool<AskPdfTool>(AskPdfTool.TheName, "Ask PDF", "Finds the passages of one or more PDFs that answer a question, with page citations.");
        services.AddPdfTool<ExtractPdfEntitiesTool>(ExtractPdfEntitiesTool.TheName, "Extract PDF Entities", "Extracts people, organizations, locations, addresses, dates, amounts and identifiers from a PDF.");
        services.AddPdfTool<ExtractPdfDataTool>(ExtractPdfDataTool.TheName, "Extract PDF Data", "Extracts user-specified fields or records from a PDF into structured JSON.");
        services.AddPdfTool<ClassifyPdfTool>(ClassifyPdfTool.TheName, "Classify PDF", "Classifies a PDF by document type or by user-defined categories.");
        services.AddPdfTool<CrossReferencePdfsTool>(CrossReferencePdfsTool.TheName, "Cross-Reference PDFs", "Finds shared information and conflicting statements across PDFs.");
    }
}
