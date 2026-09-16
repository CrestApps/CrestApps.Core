using Microsoft.Extensions.DataIngestion;

namespace CrestApps.Core.AI.Documents.Knowledge.Structure;

/// <summary>
/// Works out what a document is made of.
/// </summary>
/// <remarks>
/// Nothing here may fail an ingest. An analyzer that cannot make sense of a file says so by returning one
/// article covering all of it, which is exactly what the file was before any of this existed.
/// </remarks>
public interface IDocumentStructureAnalyzer
{
    /// <summary>
    /// Analyzes one document.
    /// </summary>
    /// <param name="document">The ingested document.</param>
    /// <returns>What the document is made of. Never <see langword="null"/>.</returns>
    DocumentStructure Analyze(IngestionDocument document);
}
