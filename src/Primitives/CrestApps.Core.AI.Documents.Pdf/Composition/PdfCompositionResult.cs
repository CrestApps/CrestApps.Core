namespace CrestApps.Core.AI.Documents.Pdf.Composition;

/// <summary>
/// A rendered composed document and what happened while rendering it.
/// </summary>
/// <param name="Bytes">The PDF file.</param>
/// <param name="PageCount">The number of pages laid out.</param>
/// <param name="Warnings">What could not be rendered as asked, in the words to report it to the reader with.</param>
internal sealed record PdfCompositionResult(byte[] Bytes, int PageCount, IReadOnlyList<string> Warnings);
