namespace CrestApps.Core.AI.Documents.Pdf.Intelligence;

/// <summary>
/// One passage a BM25 search ranked.
/// </summary>
/// <param name="Index">The passage's index in the order the index was built from.</param>
/// <param name="Score">The BM25 score.</param>
internal readonly record struct PdfBm25Hit(int Index, double Score);
