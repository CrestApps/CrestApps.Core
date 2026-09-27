namespace CrestApps.Core.AI.Documents.Pdf.Intelligence;

/// <summary>
/// The text of a PDF, page by page.
/// </summary>
/// <param name="Name">The name the document is cited by.</param>
/// <param name="PageCount">The number of pages the document has.</param>
/// <param name="Pages">The pages read, in order.</param>
internal sealed record PdfCorpusDocument(string Name, int PageCount, IReadOnlyList<PdfCorpusPage> Pages);
