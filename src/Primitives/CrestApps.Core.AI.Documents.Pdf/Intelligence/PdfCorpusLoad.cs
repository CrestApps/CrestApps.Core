namespace CrestApps.Core.AI.Documents.Pdf.Intelligence;

/// <summary>
/// The PDFs a multi-document tool read, and the ones it could not.
/// </summary>
/// <param name="Documents">The documents read.</param>
/// <param name="Skipped">One sentence per PDF that could not be read, saying why.</param>
internal sealed record PdfCorpusLoad(IReadOnlyList<PdfCorpusDocument> Documents, IReadOnlyList<string> Skipped);
